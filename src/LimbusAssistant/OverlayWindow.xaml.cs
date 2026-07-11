using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Tsundosika.LimbusAssistant;

public partial class OverlayWindow : Window
{
    const int GwlExStyle = -20;
    const int WsExTransparent = 0x20;
    const int WsExLayered = 0x80000;
    const int WsExNoActivate = 0x08000000;
    const int WsExToolWindow = 0x80;

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
        if (!IsVisible)
        {
            return;
        }
        Render(snapshot);
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
        StatusText.Text = snapshot.Status switch
        {
            CaptureStatus.GameNotFound => "Limbus Company window not found",
            CaptureStatus.WaitingForFrame => "waiting for frames…",
            _ => $"reading · confidence {snapshot.Confidence:P0}",
        };
        if (snapshot.LiveClash is { } clash)
        {
            WinRateText.Text = $"{clash.WinProbability:P0} win chance";
            var (verdict, color) = clash.WinProbability switch
            {
                >= 0.65 => ("FAVORED: take this clash", Color.FromRgb(0x7C, 0xE0, 0x7C)),
                >= 0.45 => ("EVEN: could go either way", Color.FromRgb(0xF2, 0xC9, 0x4C)),
                _ => ("RISKY: consider another skill", Color.FromRgb(0xF2, 0x6D, 0x6D)),
            };
            var brush = new SolidColorBrush(color);
            WinRateText.Foreground = brush;
            VerdictText.Foreground = brush;
            VerdictText.Text = verdict;
            DamageText.Text = $"expected damage if you win: ~{clash.ExpectedAttackPowerOnWin:F0}";
            SourceText.Text = clash.FromDataset
                ? "using full skill data"
                : "estimate from screen numbers";
        }
        else
        {
            WinRateText.Text = "…";
            WinRateText.Foreground = Brushes.White;
            VerdictText.Text = "";
            DamageText.Text = "hover a clash to see your odds";
            SourceText.Text = "";
        }
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

    [DllImport("user32.dll")]
    static extern int GetWindowLongW(IntPtr handle, int index);

    [DllImport("user32.dll")]
    static extern int SetWindowLongW(IntPtr handle, int index, int value);
}
