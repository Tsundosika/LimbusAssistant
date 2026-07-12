using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
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

    AdvisorSnapshot? _lastSnapshot;

    public OverlayWindow()
    {
        InitializeComponent();
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
                    ? $"sanity {exactSanity:+0;-0;0}, {50 + Math.Clamp(exactSanity, -45, 45)}% heads ({planning.SanitySource ?? "assumed"})"
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
                        "field" => $"sanity {sanity:+0;-0;0} read next to your sinner, {50 + Math.Clamp(sanity, -45, 45)}% heads",
                        "team" => $"{planning.IdentityName}: sanity {sanity:+0;-0;0} from your team, {50 + Math.Clamp(sanity, -45, 45)}% heads",
                        _ => $"sanity ~{sanity:+0;-0;0} (dock guess), {50 + Math.Clamp(sanity, -45, 45)}% heads. Set exact SP in Turn Advisor.",
                    }
                    : "sanity unknown, assuming 50% heads. Add your team in Turn Advisor.";
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

    void HideAll()
    {
        VerdictPanel.Visibility = Visibility.Collapsed;
        ReadOutline.Visibility = Visibility.Collapsed;
        IdleChip.Visibility = Visibility.Collapsed;
    }

    void ShowIdleChip(string text)
    {
        VerdictPanel.Visibility = Visibility.Collapsed;
        ReadOutline.Visibility = Visibility.Collapsed;
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
