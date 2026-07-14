using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Tsundosika.LimbusAssistant.Engine;
using Tsundosika.LimbusAssistant.Vision;

namespace Tsundosika.LimbusAssistant;

public partial class OverlayWindow : Window
{
    const int GwlExStyle = -20;
    const int WsExTransparent = 0x20;
    const int WsExLayered = 0x80000;
    const int WsExNoActivate = 0x08000000;
    const int WsExToolWindow = 0x80;

    static readonly SolidColorBrush GoodBrush = Frozen(0x7C, 0xE0, 0x7C);
    static readonly SolidColorBrush WarnBrush = Frozen(0xF2, 0xC9, 0x4C);
    static readonly SolidColorBrush BadBrush = Frozen(0xF2, 0x6D, 0x6D);

    static readonly SolidColorBrush MutedBrush = Frozen(0x8A, 0x90, 0xA4);

    AdvisorSnapshot? _lastSnapshot;
    readonly AppSettings _settings;
    readonly Action<AppSettings>? _saveSettings;
    readonly SoundCues _sounds;
    BestMoveReport? _lastMovesReport;
    int _lastTurnNumber = -1;
    long _turnFlashUntil;
    string? _lastPickKey;
    bool _introPersisted;

    public OverlayWindow() : this(new AppSettings(), null)
    {
    }

    public OverlayWindow(AppSettings settings, Action<AppSettings>? saveSettings)
    {
        InitializeComponent();
        _settings = settings;
        _saveSettings = saveSettings;
        _sounds = new SoundCues(settings.SoundCues);
        var scale = Math.Clamp(settings.CoachFontScale, 0.7, 2.0);
        if (Math.Abs(scale - 1.0) > 0.01)
        {
            MovesPanel.LayoutTransform = new ScaleTransform(scale, scale);
        }
        IsVisibleChanged += OnIsVisibleChanged;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongW(handle, GwlExStyle);
        SetWindowLongW(handle, GwlExStyle, style | WsExTransparent | WsExLayered | WsExNoActivate | WsExToolWindow);
    }

    public void UpdateSnapshot(AdvisorSnapshot snapshot)
    {
        _lastSnapshot = snapshot;
        if (IsVisible)
        {
            Render(snapshot);
        }
    }

    void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible && _lastSnapshot is not null)
        {
            Render(_lastSnapshot);
        }
    }

    void Render(AdvisorSnapshot snapshot)
    {
        PositionOverGame(snapshot);
        if (snapshot.Status != CaptureStatus.Ok)
        {
            if (snapshot.Status == CaptureStatus.WaitingForFrame && VerdictPanel.Visibility == Visibility.Visible)
            {
                return;
            }
            ShowIdleChip(snapshot.Status == CaptureStatus.GameNotFound
                ? "Limbus Company window not found"
                : "waiting for the game picture");
            return;
        }
        if (!snapshot.PlanningPhase)
        {
            HideAll();
            return;
        }
        RenderMoves(snapshot);
        if (snapshot.Planning is { } planning && (planning.Skill is not null || planning.RawSkillName.Length >= 3))
        {
            RenderPlanning(snapshot, planning);
            return;
        }
        RenderWatchingPanel();
    }

    void RenderPlanning(AdvisorSnapshot snapshot, PlanningHint planning)
    {
        IdleChip.Visibility = Visibility.Collapsed;
        VerdictPanel.Visibility = Visibility.Visible;
        if (planning.Skill is { } skill)
        {
            var maxRoll = skill.BasePower + skill.CoinPower * skill.CoinCount;
            KitText.Text =
                $"base {skill.BasePower}, {(skill.CoinPower >= 0 ? "+" : "")}{skill.CoinPower} per coin, " +
                $"{skill.CoinCount} coin{(skill.CoinCount == 1 ? "" : "s")}, max roll {maxRoll}";
            if (planning.IsEnemySkill)
            {
                HeadlineText.Text = $"Their attack: {skill.Name}";
                HeadlineText.Foreground = BadBrush;
                SanityText.Text = planning.EnemyName ?? "";
                if (planning.Matchups is { Count: > 0 } answers)
                {
                    MatchupsText.Visibility = Visibility.Visible;
                    MatchupsText.Text = FormatMatchups("your best answers:", answers);
                    ActionText.Text = "Send the greenest sinner into this clash.";
                }
                else
                {
                    MatchupsText.Visibility = Visibility.Collapsed;
                    ActionText.Text = "Add your team in Turn Advisor (Ctrl+F9) to see who answers this best.";
                }
            }
            else if (planning.ExactClash is { } exact)
            {
                var (verdict, brush) = exact.WinProbability switch
                {
                    >= 0.65 => ("take it", GoodBrush),
                    >= 0.45 => ("close call", WarnBrush),
                    _ => ("risky", BadBrush),
                };
                HeadlineText.Text = $"{exact.WinProbability:P0} win, {verdict}";
                HeadlineText.Foreground = brush;
                KitText.Text =
                    $"{skill.Name} vs {planning.ExactEnemySkillName}" +
                    $" · deal ~{exact.ExpectedDamageDealt:F0} · take ~{exact.ExpectedDamageTaken:F0}";
                SanityText.Text = planning.Sanity is { } exactSanity
                    ? $"sanity {exactSanity:+0;-0;0}, {50 + Math.Clamp(exactSanity, -45, 45)}% heads ({planning.SanitySource ?? "assumed"}){StaleSuffix(planning)}"
                    : "sanity unknown, assuming 50% heads";
                if (planning.Matchups is { Count: > 0 } rest)
                {
                    MatchupsText.Visibility = Visibility.Visible;
                    MatchupsText.Text = FormatMatchups($"other skills of {planning.EnemyName}:", rest);
                }
                else
                {
                    MatchupsText.Visibility = Visibility.Collapsed;
                }
                ActionText.Text = "Exact clash read from both tooltips, full coin math.";
            }
            else
            {
                HeadlineText.Text = skill.Name;
                HeadlineText.Foreground = GoodBrush;
                SanityText.Text = planning.Sanity is { } sanity
                    ? planning.SanitySource switch
                    {
                        "field" => $"sanity {sanity:+0;-0;0} read next to your sinner, {50 + Math.Clamp(sanity, -45, 45)}% heads{StaleSuffix(planning)}",
                        "team" => $"{planning.IdentityName}: sanity {sanity:+0;-0;0} from your team, {50 + Math.Clamp(sanity, -45, 45)}% heads{StaleSuffix(planning)}",
                        "dock slot" => $"sanity {sanity:+0;-0;0} from the dock, {50 + Math.Clamp(sanity, -45, 45)}% heads{StaleSuffix(planning)}",
                        "dock rank" => $"sanity {sanity:+0;-0;0} via dock order (circle hidden by glow), {50 + Math.Clamp(sanity, -45, 45)}% heads{StaleSuffix(planning)}",
                        _ => $"sanity ~{sanity:+0;-0;0}, {50 + Math.Clamp(sanity, -45, 45)}% heads{StaleSuffix(planning)}",
                    }
                    : "sanity unknown, assuming 50% heads";
                if (planning.Matchups is { Count: > 0 } matchups)
                {
                    MatchupsText.Visibility = Visibility.Visible;
                    MatchupsText.Text = FormatMatchups($"vs {planning.EnemyName}:", matchups);
                    ActionText.Text = "Green clashes are safe picks. Red means guard or reroute.";
                }
                else
                {
                    MatchupsText.Visibility = Visibility.Collapsed;
                    ActionText.Text = planning.EnemyName is null
                        ? "Hover the enemy or pick them in Turn Advisor to see win odds."
                        : $"No skill data for {planning.EnemyName}.";
                }
            }
        }
        else
        {
            HeadlineText.Text = planning.RawSkillName;
            HeadlineText.Foreground = WarnBrush;
            KitText.Text = "skill not found in the dataset";
            SanityText.Text = "";
            MatchupsText.Visibility = Visibility.Collapsed;
            ActionText.Text = "Refresh the dataset with the wiki importer to cover every identity.";
        }
        PlacePanel();
        PlaceOutline(snapshot, planning.Confidence);
        ApplyCoachHint(snapshot, planning);
    }

    void ApplyCoachHint(AdvisorSnapshot snapshot, PlanningHint planning)
    {
        if (planning.IsEnemySkill
            || snapshot.BestMoves is not { } report
            || planning.IdentityName is null
            || planning.Skill is null)
        {
            return;
        }
        var move = report.Moves.FirstOrDefault(candidate => candidate.IdentityName == planning.IdentityName);
        if (move is null)
        {
            return;
        }
        if (move.SkillName == planning.Skill.Name)
        {
            ActionText.Text = "This is the coach's pick for this sinner. Go for it.";
            if (ReadOutline.Visibility == Visibility.Visible)
            {
                ReadOutline.Stroke = GoodBrush;
            }
            var pickKey = $"{move.IdentityName}|{move.SkillName}";
            if (pickKey != _lastPickKey)
            {
                _lastPickKey = pickKey;
                _sounds.CorrectPick();
            }
        }
        else
        {
            ActionText.Text = $"Coach suggests Skill {move.SkillNumber} ({move.SkillName}) for {move.Sinner} instead.";
        }
    }

    void PlacePanel()
    {
        VerdictPanel.Measure(new Size(340, double.PositiveInfinity));
        var desired = VerdictPanel.DesiredSize;
        Canvas.SetLeft(VerdictPanel, Math.Max(8, Width - desired.Width - 20));
        Canvas.SetTop(VerdictPanel, Math.Clamp(Height * 0.30, 8, Math.Max(8, Height - desired.Height - 8)));
    }

    void RenderWatchingPanel()
    {
        IdleChip.Visibility = Visibility.Collapsed;
        ReadOutline.Visibility = Visibility.Collapsed;
        VerdictPanel.Visibility = Visibility.Visible;
        HeadlineText.Text = "watching";
        HeadlineText.Foreground = WarnBrush;
        KitText.Text = "hover a skill or a queued clash to read it";
        SanityText.Text = "";
        MatchupsText.Visibility = Visibility.Collapsed;
        ActionText.Text = "the panel fills in as soon as a tooltip is visible";
        PlacePanel();
    }

    void PlaceOutline(AdvisorSnapshot snapshot, double confidence)
    {
        if (!TryMapRegion(snapshot, RegionNames.DragSkillName, out var ribbon))
        {
            ReadOutline.Visibility = Visibility.Collapsed;
            return;
        }
        ReadOutline.Visibility = Visibility.Visible;
        ReadOutline.Stroke = confidence >= 0.8 ? GoodBrush : confidence >= 0.5 ? WarnBrush : BadBrush;
        ReadOutline.Width = ribbon.Width + 8;
        ReadOutline.Height = ribbon.Height + 8;
        Canvas.SetLeft(ReadOutline, ribbon.Left - 4);
        Canvas.SetTop(ReadOutline, ribbon.Top - 4);
    }

    static string StaleSuffix(PlanningHint planning) =>
        planning.SanityAgeSeconds is { } age and >= 10 ? $", read {age}s ago" : "";

    static string FormatMatchups(string? header, IReadOnlyList<MatchupOdds> matchups)
    {
        var lines = new List<string>();
        if (header is not null)
        {
            lines.Add(header);
        }
        foreach (var matchup in matchups.Take(5))
        {
            var icon = matchup.WinProbability switch
            {
                >= 0.65 => "🟢",
                >= 0.45 => "🟡",
                _ => "🔴",
            };
            lines.Add($"{icon} {Truncate(matchup.EnemySkillName, 16),-16} {matchup.WinProbability,4:P0}  deal {matchup.ExpectedDamageDealt:F0}");
        }
        return string.Join(Environment.NewLine, lines);
    }

    static string Truncate(string text, int length) =>
        text.Length <= length ? text : text[..(length - 1)] + "…";

    bool TryMapRegion(AdvisorSnapshot snapshot, string regionName, out Rect mapped)
    {
        mapped = default;
        if (snapshot.Reading.FrameWidth <= 0
            || !snapshot.Reading.Regions.TryGetValue(regionName, out var region))
        {
            return false;
        }
        var scaleX = Width / snapshot.Reading.FrameWidth;
        var scaleY = Height / snapshot.Reading.FrameHeight;
        mapped = new Rect(region.X * scaleX, region.Y * scaleY, region.Width * scaleX, region.Height * scaleY);
        return true;
    }

    void RenderMoves(AdvisorSnapshot snapshot)
    {
        var report = snapshot.BestMoves;
        if (report is null || report.Moves.Count == 0 || !snapshot.PlanningLiveNow)
        {
            MovesPanel.Visibility = Visibility.Collapsed;
            return;
        }
        MovesPanel.Visibility = Visibility.Visible;
        var coach = snapshot.Coach ?? CoachProgressState.Empty;
        var now = Environment.TickCount64;
        if (coach.TurnNumber != _lastTurnNumber)
        {
            _lastTurnNumber = coach.TurnNumber;
            _turnFlashUntil = now + 2500;
            _sounds.NewPlan();
        }
        else if (!ReferenceEquals(report, _lastMovesReport))
        {
            _sounds.NewPlan();
        }
        _lastMovesReport = report;
        MovesHeadline.Text = now < _turnFlashUntil
            ? "New turn, new plan"
            : $"Best moves ({coach.DoneCount} of {Math.Max(coach.Total, report.Moves.Count)} done)";
        RenderNowInstruction(report, coach);
        RenderChecklist(report, coach);
        RenderFooter(report);
        RenderHint();
        PlaceMovesPanel();
    }

    void RenderNowInstruction(BestMoveReport report, CoachProgressState coach)
    {
        var currentIndex = coach.CurrentIndex >= 0 && coach.CurrentIndex < report.Moves.Count
            ? coach.CurrentIndex
            : coach.Total == 0 ? 0 : -1;
        if (currentIndex < 0)
        {
            NowText.Text = "All moves assigned. Hit To Battle!";
            NowText.Foreground = GoodBrush;
            NowDetailText.Visibility = Visibility.Collapsed;
            return;
        }
        var move = report.Moves[currentIndex];
        NowText.Text = $"NOW: {CoachText.Instruction(move, _settings.PlainLanguage)}";
        NowText.Foreground = move.IsUnopposed || move.WinProbability >= 0.65
            ? GoodBrush
            : move.WinProbability >= 0.45 ? WarnBrush : BadBrush;
        var details = new List<string>();
        if (CoachText.Why(move) is { } why)
        {
            details.Add(why);
        }
        if (CoachText.Fallback(move, _settings.PlainLanguage) is { } fallback)
        {
            details.Add(fallback);
        }
        NowDetailText.Text = string.Join("  ·  ", details);
        NowDetailText.Visibility = details.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    void RenderChecklist(BestMoveReport report, CoachProgressState coach)
    {
        MovesStack.Children.Clear();
        for (var i = 0; i < report.Moves.Count; i++)
        {
            var move = report.Moves[i];
            var done = i < coach.Done.Count && coach.Done[i];
            var isCurrent = i == coach.CurrentIndex;
            var mark = done ? "✔" : isCurrent ? "▶" : "○";
            var target = move.IsUnopposed ? "free hit" : $"vs {Truncate(move.TargetSkillName ?? "", 16)}";
            var line = new TextBlock
            {
                Text = $"{mark} {i + 1}. {move.Sinner}: Skill {move.SkillNumber} {target}",
                Foreground = done
                    ? MutedBrush
                    : move.IsUnopposed || move.WinProbability >= 0.65
                        ? GoodBrush
                        : move.WinProbability >= 0.45 ? WarnBrush : BadBrush,
                FontSize = isCurrent ? 13 : 12,
                FontWeight = isCurrent ? FontWeights.Bold : FontWeights.Normal,
                Margin = new Thickness(0, 2, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            };
            MovesStack.Children.Add(line);
        }
    }

    void RenderFooter(BestMoveReport report)
    {
        if (report.Unblocked.Count == 0)
        {
            MovesFooter.Visibility = Visibility.Collapsed;
            return;
        }
        MovesFooter.Visibility = Visibility.Visible;
        var worst = report.Unblocked[0];
        var warning = CoachText.UnblockedWarning(worst, _settings.PlainLanguage);
        MovesFooter.Text = report.Unblocked.Count > 1
            ? $"{warning} ({report.Unblocked.Count - 1} more unblocked)"
            : warning;
    }

    void RenderHint()
    {
        if (!_settings.ShownCoachIntro)
        {
            MovesHint.Visibility = Visibility.Visible;
            MovesHint.Text = "Follow the NOW line, one move at a time. It ticks off by itself when you assign the move. " +
                $"Stuck? {_settings.CoachAdvanceHotkey} skips to the next one.";
            if (!_introPersisted && _saveSettings is not null)
            {
                _introPersisted = true;
                _saveSettings(_settings with { ShownCoachIntro = true });
            }
            return;
        }
        MovesHint.Visibility = Visibility.Collapsed;
    }

    void PlaceMovesPanel()
    {
        MovesPanel.Measure(new Size(380, double.PositiveInfinity));
        var desired = MovesPanel.DesiredSize;
        switch (_settings.CoachPanelPosition.ToLowerInvariant())
        {
            case "right":
                Canvas.SetLeft(MovesPanel, Math.Max(8, Width - desired.Width - 20));
                Canvas.SetTop(MovesPanel, 12);
                break;
            case "top":
                Canvas.SetLeft(MovesPanel, Math.Max(8, (Width - desired.Width) / 2));
                Canvas.SetTop(MovesPanel, 12);
                break;
            default:
                Canvas.SetLeft(MovesPanel, 24);
                Canvas.SetTop(MovesPanel, Math.Clamp(Height * 0.28, 8, Math.Max(8, Height - desired.Height - 8)));
                break;
        }
    }

    void HideAll()
    {
        VerdictPanel.Visibility = Visibility.Collapsed;
        ReadOutline.Visibility = Visibility.Collapsed;
        IdleChip.Visibility = Visibility.Collapsed;
        MovesPanel.Visibility = Visibility.Collapsed;
    }

    void ShowIdleChip(string text)
    {
        VerdictPanel.Visibility = Visibility.Collapsed;
        ReadOutline.Visibility = Visibility.Collapsed;
        MovesPanel.Visibility = Visibility.Collapsed;
        IdleChip.Visibility = Visibility.Visible;
        IdleChipText.Text = text;
        IdleChip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(IdleChip, Math.Max(8, Width - IdleChip.DesiredSize.Width - 16));
        Canvas.SetTop(IdleChip, 12);
    }

    void PositionOverGame(AdvisorSnapshot snapshot)
    {
        if (snapshot.GameBounds is not { } bounds)
        {
            return;
        }
        if (PresentationSource.FromVisual(this)?.CompositionTarget is not { } target)
        {
            return;
        }
        var transform = target.TransformFromDevice;
        var topLeft = transform.Transform(new Point(bounds.X, bounds.Y));
        var size = transform.Transform(new Point(bounds.Width, bounds.Height));
        Left = topLeft.X;
        Top = topLeft.Y;
        Width = Math.Max(1, size.X);
        Height = Math.Max(1, size.Y);
    }

    static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    [DllImport("user32.dll")]
    static extern int GetWindowLongW(IntPtr handle, int index);

    [DllImport("user32.dll")]
    static extern int SetWindowLongW(IntPtr handle, int index, int value);
}
