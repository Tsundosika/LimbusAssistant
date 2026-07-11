using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

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
            WinRateText.Text = $"{clash.WinProbability:P0} win";
            DamageText.Text = $"~{clash.ExpectedAttackPowerOnWin:F1} attack power on win";
            SourceText.Text = clash.FromDataset
                ? "matched skill data (full coin math)"
                : "screen numbers only — coin power unknown";
        }
        else
        {
            WinRateText.Text = "—";
            DamageText.Text = "no clash detected";
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
