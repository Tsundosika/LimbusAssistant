using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Tsundosika.LimbusAssistant.Engine;
using Tsundosika.LimbusAssistant.Vision;

namespace Tsundosika.LimbusAssistant;

public partial class MainWindow : Window
{
    const string AutoOption = "Auto: find the game for me (recommended)";

    readonly GameData _data;
    readonly CalibrationProfile _profile;
    readonly ClashCalculator _calculator = new();
    readonly TurnSolver _solver = new();
    readonly ObservableCollection<TeamEntry> _team = [];
    WriteableBitmap? _bitmap;
    bool _suppressSelection;

    public event Action<string?>? GameWindowSelected;

    public event Action? OverlayToggleRequested;

    public event Action<EnemyData?>? LiveEnemySelected;

    public event Action<IReadOnlyList<(string Name, int Sanity)>>? TeamChanged;

    public EnemyData? SelectedEnemy => EnemyList.SelectedItem as EnemyData;

    public IReadOnlyList<(string Name, int Sanity)> TeamMembers =>
        _team.Select(entry => (entry.Identity.Name, entry.Sanity)).ToList();

    public bool PlainLanguage { get; set; } = true;

    public void SeedTeam(IEnumerable<(string Name, int Sanity)> members)
    {
        foreach (var (name, sanity) in members)
        {
            var identity = _data.Identities.FirstOrDefault(candidate => candidate.Name == name);
            if (identity is not null && _team.All(entry => entry.Identity.Name != name))
            {
                _team.Add(new TeamEntry(identity, sanity));
            }
        }
    }

    public MainWindow(GameData data, CalibrationProfile profile)
    {
        InitializeComponent();
        _data = data;
        _profile = profile;
        IdentityCombo.ItemsSource = data.Identities;
        if (data.Identities.Count > 0)
        {
            IdentityCombo.SelectedIndex = 0;
        }
        TeamList.ItemsSource = _team;
        _team.CollectionChanged += (_, _) => TeamChanged?.Invoke(TeamMembers);
        RefreshEnemyList("");
        PopulateWindowList();
    }

    AdvisorSnapshot? _lastSnapshot;

    public void UpdateSnapshot(AdvisorSnapshot snapshot)
    {
        _lastSnapshot = snapshot;
        (StatusHeadline.Text, StatusHeadline.Foreground, StatusDetail.Text) = snapshot.Status switch
        {
            CaptureStatus.GameNotFound => (
                "Looking for the game",
                (Brush)FindResource("AccentBrush"),
                "Start Limbus Company, or pick its window below."),
            CaptureStatus.WaitingForFrame => (
                "Connected, waiting for the picture",
                (Brush)FindResource("AccentBrush"),
                "The game window was found. If this takes long, make sure the window is not minimized."),
            _ => (
                "Connected ✓",
                (Brush)FindResource("GoodBrush"),
                $"Watching the game. Vision confidence {snapshot.Confidence:P0}. Press Ctrl+F8 in battle to see the advisor."),
        };
        if (!IsVisible || !ReferenceEquals(Tabs.SelectedItem, VisionTab))
        {
            return;
        }
        RenderVision(snapshot);
    }

    void OnTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(Tabs.SelectedItem, VisionTab) && _lastSnapshot is not null)
        {
            RenderVision(_lastSnapshot);
        }
    }

    void RenderVision(AdvisorSnapshot snapshot)
    {
        ConfidenceText.Text =
            $"tick {snapshot.Metrics.AverageTickMilliseconds:F1} ms avg · {snapshot.Metrics.OcrCallsLastTick} OCR calls · " +
            $"gate {(snapshot.ClashGateOpen ? "open" : "closed")} · {snapshot.Timestamp:HH:mm:ss}";
        ReadingsText.Text = FormatReadings(snapshot);
        if (snapshot.Frame is { } frame)
        {
            UpdateFrame(frame, snapshot.Reading);
        }
    }

    public void SetOverlayVisible(bool visible) =>
        OverlayButton.Content = visible
            ? "Hide overlay  (Ctrl+F8)"
            : "Show overlay in game  (Ctrl+F8)";

    void PopulateWindowList()
    {
        _suppressSelection = true;
        var items = new List<object> { AutoOption };
        items.AddRange(WindowEnumerator.ListCaptureCandidates());
        WindowCombo.ItemsSource = items;
        WindowCombo.SelectedIndex = 0;
        _suppressSelection = false;
    }

    void OnRefreshWindowsClick(object sender, RoutedEventArgs e) => PopulateWindowList();

    void OnWindowSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection)
        {
            return;
        }
        GameWindowSelected?.Invoke(WindowCombo.SelectedItem as WindowInfo is { } window ? window.Title : null);
    }

    void OnOverlayButtonClick(object sender, RoutedEventArgs e) => OverlayToggleRequested?.Invoke();

    void OnCalculateClashClick(object sender, RoutedEventArgs e)
    {
        var valid = TryReadInt(AllyBaseBox, -99, 999, out var allyBase)
            & TryReadInt(AllyCoinBox, -99, 99, out var allyCoin)
            & TryReadInt(AllyCountBox, 0, 30, out var allyCount)
            & TryReadInt(AllySanityBox, ClashSkill.MinSanity, ClashSkill.MaxSanity, out var allySanity)
            & TryReadInt(AllyModifierBox, -99, 99, out var allyModifier)
            & TryReadInt(AllyParalyzeBox, 0, 99, out var allyParalyze)
            & TryReadInt(EnemyBaseBox, -99, 999, out var enemyBase)
            & TryReadInt(EnemyCoinBox, -99, 99, out var enemyCoin)
            & TryReadInt(EnemyCountBox, 0, 30, out var enemyCount)
            & TryReadInt(EnemySanityBox, ClashSkill.MinSanity, ClashSkill.MaxSanity, out var enemySanity)
            & TryReadInt(EnemyModifierBox, -99, 99, out var enemyModifier)
            & TryReadInt(EnemyParalyzeBox, 0, 99, out var enemyParalyze);
        if (!valid)
        {
            ClashResultHeadline.Text = "Fix the highlighted fields";
            ClashResultHeadline.Foreground = (Brush)FindResource("BadBrush");
            ClashGameStyleLine.Text = "";
            ClashResultDetail.Text = "Every box needs a whole number. Coins 0 to 30, sanity -45 to 45.";
            return;
        }
        var ally = new ClashSkill(allyBase, allyCoin, allyCount, allySanity, allyParalyze, allyModifier);
        var enemy = new ClashSkill(enemyBase, enemyCoin, enemyCount, enemySanity, enemyParalyze, enemyModifier);
        var outcome = _calculator.Calculate(ally, enemy);
        ClashGameStyleLine.Text =
            $"the game will show about {ClashCalculator.FirstExchangeWinProbability(ally, enemy):P1} " +
            "(first exchange only, ignores the rest of the clash)";
        var attackPower = ExpectedAttackPower.OnClashWin(ally, outcome.WinStates);
        var takenPower = ExpectedAttackPower.OnClashWin(enemy, outcome.LoseStates);
        var (verdict, brushKey) = outcome.EffectiveWinProbability switch
        {
            >= 0.65 => ("favored", "GoodBrush"),
            >= 0.45 => ("even", "WarnBrush"),
            _ => ("risky", "BadBrush"),
        };
        ClashResultHeadline.Text = $"{outcome.EffectiveWinProbability:P1} to win ({verdict})";
        ClashResultHeadline.Foreground = (Brush)FindResource(brushKey);
        var detail = new StringBuilder();
        detail.AppendLine($"you win the clash      {outcome.WinProbability,8:P2}");
        detail.AppendLine($"enemy wins the clash   {outcome.LoseProbability,8:P2}");
        if (outcome.UnresolvedProbability > 0.0001)
        {
            detail.AppendLine($"endless stalemate      {outcome.UnresolvedProbability,8:P2}");
        }
        detail.AppendLine($"your coins left on win {outcome.ExpectedCoinsOnWin,8:F2}");
        detail.AppendLine($"attack power you land  {attackPower,8:F2}");
        detail.AppendLine($"attack power you eat   {takenPower,8:F2}");
        ClashResultDetail.Text = detail.ToString();
    }

    void OnAddTeamMemberClick(object sender, RoutedEventArgs e)
    {
        if (IdentityCombo.SelectedItem is not IdentityData identity)
        {
            return;
        }
        if (!TryReadInt(TeamSanityBox, ClashSkill.MinSanity, ClashSkill.MaxSanity, out var sanity))
        {
            return;
        }
        _team.Add(new TeamEntry(identity, sanity));
    }

    void OnRemoveTeamMemberClick(object sender, RoutedEventArgs e)
    {
        if (TeamList.SelectedItem is TeamEntry entry)
        {
            _team.Remove(entry);
        }
    }

    void OnEnemySearchChanged(object sender, TextChangedEventArgs e) => RefreshEnemyList(EnemySearchBox.Text);

    void OnEnemySelectionChanged(object sender, SelectionChangedEventArgs e) =>
        LiveEnemySelected?.Invoke(EnemyList.SelectedItem as EnemyData);

    void RefreshEnemyList(string query)
    {
        var trimmed = query.Trim();
        var matches = _data.Enemies
            .Where(enemy => trimmed.Length == 0
                || enemy.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
            .ToList();
        EnemyList.ItemsSource = matches;
        if (matches.Count > 0)
        {
            EnemyList.SelectedIndex = 0;
        }
    }

    void OnPlanTurnClick(object sender, RoutedEventArgs e)
    {
        var enemies = EnemyList.SelectedItems.OfType<EnemyData>().ToList();
        if (enemies.Count == 0)
        {
            PlanHeadline.Text = "Pick an enemy first";
            return;
        }
        if (_team.Count == 0)
        {
            PlanHeadline.Text = "Add at least one identity to your team";
            return;
        }
        var units = _team.Select(entry => new TurnUnit(entry.Identity, entry.Sanity)).ToList();
        var report = BestMoveAdvisor.Advise(_solver, units, enemies);
        PlanHeadline.Text = enemies.Count == 1
            ? $"Best moves vs {enemies[0].Name}"
            : $"Best moves vs {enemies.Count} enemies";
        var summary = $"Total expected value {report.TotalExpectedValue:F1} (damage you deal minus damage you take).";
        if (report.Unblocked.Count > 0)
        {
            summary += "  Heads up: " + CoachText.UnblockedWarning(report.Unblocked[0], PlainLanguage);
        }
        PlanSummary.Text = summary;
        PlanList.ItemsSource = report.Moves.Select((move, index) => Describe(index + 1, move)).ToList();
    }

    string Describe(int index, BestMoveAdvice move)
    {
        var icon = move.IsUnopposed ? "⚔" : move.WinProbability switch
        {
            >= 0.65 => "🟢",
            >= 0.45 => "🟡",
            _ => "🔴",
        };
        var lines = new List<string> { $"{icon}  {index}. {CoachText.Instruction(move, PlainLanguage)}" };
        if (CoachText.Why(move) is { } why)
        {
            lines.Add($"    {why}");
        }
        if (CoachText.Fallback(move, PlainLanguage) is { } fallback)
        {
            lines.Add($"    {fallback}");
        }
        if (!PlainLanguage && !move.IsUnopposed)
        {
            lines.Add($"    win {move.WinProbability:P0} · deal ~{move.ExpectedDamageDealt:F1} · take ~{move.ExpectedDamageTaken:F1}");
        }
        return string.Join("\n", lines);
    }

    bool TryReadInt(TextBox box, int min, int max, out int value)
    {
        var valid = int.TryParse(box.Text.Trim(), out value) && value >= min && value <= max;
        box.BorderBrush = (Brush)FindResource(valid ? "FieldBorderBrush" : "BadBrush");
        box.BorderThickness = new Thickness(valid ? 1 : 2);
        return valid;
    }

    static string FormatReadings(AdvisorSnapshot snapshot)
    {
        var builder = new StringBuilder();
        if (snapshot.LiveClash is { } clash)
        {
            builder.AppendLine(
                $"live clash: win {clash.WinProbability:P1} " +
                $"(game shows about {clash.FirstExchangeWinProbability:P1}), " +
                $"power {clash.ExpectedAttackPowerOnWin:F1}");
            builder.AppendLine($"  source: {(clash.FromDataset ? "dataset" : "screen numbers")} · confidence {clash.Confidence:P0}");
            builder.AppendLine();
        }
        if (snapshot.Planning is { } planning)
        {
            builder.AppendLine($"planning: \"{planning.RawSkillName}\" ({planning.Confidence:P0})");
            builder.AppendLine(planning.Skill is { } skill
                ? $"  matched {skill.Name} ({planning.IdentityName}) base {skill.BasePower} +{skill.CoinPower} x{skill.CoinCount}"
                : "  no dataset match");
            builder.AppendLine(planning.Sanity is { } sanity ? $"  sanity {sanity}" : "  sanity unknown");
            builder.AppendLine();
        }
        if (snapshot.Reading.FrameWidth > 0)
        {
            var content = snapshot.Reading.ContentRect;
            if (content.X > 0 || content.Y > 0)
            {
                builder.AppendLine($"letterbox detected: content {content.Width}x{content.Height} at ({content.X},{content.Y})");
                builder.AppendLine();
            }
        }
        foreach (var (name, reading) in snapshot.Reading.Texts.OrderBy(pair => pair.Key))
        {
            builder.AppendLine($"{name,-24} \"{reading.Text}\"  ({reading.Confidence:P0})");
        }
        foreach (var (name, reading) in snapshot.Reading.Numbers.OrderBy(pair => pair.Key))
        {
            var value = reading.Value?.ToString() ?? "?";
            builder.AppendLine($"{name,-24} {value,6}  ({reading.Confidence:P0})");
        }
        foreach (var (name, reading) in snapshot.Reading.Icons.OrderBy(pair => pair.Key))
        {
            builder.AppendLine($"{name,-24} {reading.Name ?? "?",12}  ({reading.Confidence:P0})");
        }
        return builder.ToString();
    }

    void UpdateFrame(CaptureFrame frame, VisionReading reading)
    {
        if (_bitmap is null || _bitmap.PixelWidth != frame.Width || _bitmap.PixelHeight != frame.Height)
        {
            _bitmap = new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null);
            FrameImage.Source = _bitmap;
            RegionCanvas.Width = frame.Width;
            RegionCanvas.Height = frame.Height;
        }
        _bitmap.WritePixels(new Int32Rect(0, 0, frame.Width, frame.Height), frame.PixelsBgra, frame.Stride, 0);
        DrawRegions(reading, frame.Height);
    }

    void DrawRegions(VisionReading reading, int frameHeight)
    {
        RegionCanvas.Children.Clear();
        if (reading.Regions.Count > 0)
        {
            foreach (var region in _profile.Regions)
            {
                if (!reading.Regions.TryGetValue(region.Name, out var rect))
                {
                    rect = region.Rect.ToPixelsWithin(reading.ContentRect);
                }
                DrawRegion(region, rect, frameHeight);
            }
            return;
        }
        foreach (var region in _profile.Regions)
        {
            var rect = region.Rect.ToPixelsWithin(reading.ContentRect);
            DrawRegion(region, rect, frameHeight);
        }
    }

    void DrawRegion(AnchorRegion region, PixelRect rect, int frameHeight)
    {
        var box = new Rectangle
        {
            Width = rect.Width,
            Height = rect.Height,
            Stroke = region.Kind == RegionKind.Number ? Brushes.OrangeRed : Brushes.DeepSkyBlue,
            StrokeThickness = 2,
        };
        Canvas.SetLeft(box, rect.X);
        Canvas.SetTop(box, rect.Y);
        RegionCanvas.Children.Add(box);
        var label = new TextBlock
        {
            Text = region.Name,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)),
            FontSize = Math.Max(10, frameHeight / 90.0),
        };
        Canvas.SetLeft(label, rect.X);
        Canvas.SetTop(label, Math.Max(0, rect.Y - frameHeight / 60.0));
        RegionCanvas.Children.Add(label);
    }

    sealed record TeamEntry(IdentityData Identity, int Sanity)
    {
        public override string ToString() => $"{Identity.Name}   (sanity {Sanity:+0;-0;0})";
    }
}
