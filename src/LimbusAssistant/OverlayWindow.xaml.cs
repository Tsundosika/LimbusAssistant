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
            ShowIdleChip(snapshot.Status == CaptureStatus.GameNotFound
                ? "Limbus Company window not found"
                : "waiting for the game picture");
            return;
        }
        if (!snapshot.ClashGateOpen || snapshot.Planning is null)
        {
            ShowIdleChip("watching for your next move");
            return;
        }
        RenderPlanning(snapshot, snapshot.Planning);
    }

    void RenderPlanning(AdvisorSnapshot snapshot, PlanningHint planning)
    {
        IdleChip.Visibility = Visibility.Collapsed;
        VerdictPanel.Visibility = Visibility.Visible;
        if (planning.Skill is { } skill)
        {
            HeadlineText.Text = skill.Name;
            HeadlineText.Foreground = GoodBrush;
            var maxRoll = skill.BasePower + skill.CoinPower * skill.CoinCount;
            KitText.Text =
                $"base {skill.BasePower}, {(skill.CoinPower >= 0 ? "+" : "")}{skill.CoinPower} per coin, " +
                $"{skill.CoinCount} coin{(skill.CoinCount == 1 ? "" : "s")}, max roll {maxRoll}";
            SanityText.Text = planning.Sanity is { } sanity
                ? $"sanity {sanity:+0;-0;0} means {50 + Math.Clamp(sanity, -45, 45)}% heads per coin"
                : "sanity not read, assume 50% heads";
            ActionText.Text = "Exact clash odds vs any enemy skill: Turn Advisor (Ctrl+F9).";
        }
        else
        {
            HeadlineText.Text = planning.RawSkillName;
            HeadlineText.Foreground = WarnBrush;
            KitText.Text = "skill not found in the dataset";
            SanityText.Text = "";
            ActionText.Text = "Add this identity to Data/identities.json to get full coin math.";
        }
        PlacePanel(snapshot);
        PlaceOutline(snapshot, planning.Confidence);
    }

    void PlacePanel(AdvisorSnapshot snapshot)
    {
        VerdictPanel.Measure(new Size(340, double.PositiveInfinity));
        var desired = VerdictPanel.DesiredSize;
        if (!TryMapRegion(snapshot, RegionNames.DragSkillName, out var ribbon))
        {
            Canvas.SetLeft(VerdictPanel, Math.Max(8, Width - desired.Width - 24));
            Canvas.SetTop(VerdictPanel, 24);
            return;
        }
        var left = Math.Clamp(ribbon.Right + 16, 8, Math.Max(8, Width - desired.Width - 8));
        var top = Math.Clamp(ribbon.Top - 4, 8, Math.Max(8, Height - desired.Height - 8));
        Canvas.SetLeft(VerdictPanel, left);
        Canvas.SetTop(VerdictPanel, top);
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
