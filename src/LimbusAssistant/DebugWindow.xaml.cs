using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Tsundosika.LimbusAssistant.Engine;
using Tsundosika.LimbusAssistant.Vision;

namespace Tsundosika.LimbusAssistant;

public partial class DebugWindow : Window
{
    readonly GameData _data;
    readonly CalibrationProfile _profile;
    readonly Recommender _recommender = new();
    WriteableBitmap? _bitmap;

    public DebugWindow(GameData data, CalibrationProfile profile)
    {
        InitializeComponent();
        _data = data;
        _profile = profile;
        IdentityCombo.ItemsSource = data.Identities;
        EnemyCombo.ItemsSource = data.Enemies;
        if (data.Identities.Count > 0)
        {
            IdentityCombo.SelectedIndex = 0;
        }
        if (data.Enemies.Count > 0)
        {
            EnemyCombo.SelectedIndex = 0;
        }
    }

    public void UpdateSnapshot(AdvisorSnapshot snapshot)
    {
        StatusText.Text = snapshot.Status switch
        {
            CaptureStatus.GameNotFound => "Game window not found — is Limbus Company running?",
            CaptureStatus.WaitingForFrame => "Game found, waiting for capture frames…",
            _ => "Capturing",
        };
        ConfidenceText.Text = $"vision confidence {snapshot.Confidence:P0} · {snapshot.Timestamp:HH:mm:ss}";
        ReadingsText.Text = FormatReadings(snapshot);
        if (snapshot.Frame is { } frame)
        {
            UpdateFrame(frame);
        }
    }

    static string FormatReadings(AdvisorSnapshot snapshot)
    {
        var builder = new StringBuilder();
        if (snapshot.LiveClash is { } clash)
        {
            builder.AppendLine($"live clash: win {clash.WinProbability:P1}, power {clash.ExpectedAttackPowerOnWin:F1}");
            builder.AppendLine($"  source: {(clash.FromDataset ? "dataset" : "screen numbers")} · confidence {clash.Confidence:P0}");
            builder.AppendLine();
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

    void UpdateFrame(CaptureFrame frame)
    {
        if (_bitmap is null || _bitmap.PixelWidth != frame.Width || _bitmap.PixelHeight != frame.Height)
        {
            _bitmap = new WriteableBitmap(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null);
            FrameImage.Source = _bitmap;
            RegionCanvas.Width = frame.Width;
            RegionCanvas.Height = frame.Height;
            DrawRegions(frame.Width, frame.Height);
        }
        _bitmap.WritePixels(new Int32Rect(0, 0, frame.Width, frame.Height), frame.PixelsBgra, frame.Stride, 0);
    }

    void DrawRegions(int frameWidth, int frameHeight)
    {
        RegionCanvas.Children.Clear();
        foreach (var region in _profile.Regions)
        {
            var rect = region.Rect.ToPixels(frameWidth, frameHeight);
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
    }

    void OnEvaluateClick(object sender, RoutedEventArgs e)
    {
        if (IdentityCombo.SelectedItem is not IdentityData identity || EnemyCombo.SelectedItem is not EnemyData enemy)
        {
            return;
        }
        if (!int.TryParse(SanityBox.Text, out var sanity))
        {
            sanity = 0;
        }
        sanity = Math.Clamp(sanity, ClashSkill.MinSanity, ClashSkill.MaxSanity);
        var allies = identity.Skills.Select((skill, index) => new ClashCandidate(identity.Sinner, skill, sanity, index));
        var threats = enemy.Skills.Select(skill => new EnemyThreat(enemy, skill));
        var ranked = _recommender.Rank(allies, threats, 12);
        SuggestionsList.ItemsSource = ranked
            .Select(suggestion =>
                $"{suggestion.Ally.Skill.Name} vs {suggestion.Threat.Skill.Name}: " +
                $"win {suggestion.WinProbability:P0} · dmg {suggestion.ExpectedDamage:F1}")
            .ToList();
    }
}
