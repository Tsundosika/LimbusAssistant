using System.IO;
using System.Windows;
using Tsundosika.LimbusAssistant.Engine;
using Tsundosika.LimbusAssistant.Vision;

namespace Tsundosika.LimbusAssistant;

public partial class App : Application
{
    AppSettings? _settings;
    AdvisorLoop? _loop;
    HotkeyManager? _hotkeys;
    OverlayWindow? _overlay;
    DebugWindow? _debug;
    TemplateLibrary? _templates;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _settings = AppSettings.Load();
        _settings.Save();
        var data = GameData.Load(Path.Combine(AppContext.BaseDirectory, "Data"));
        var calibration = CalibrationStore.Load();
        _templates = TemplateLibrary.LoadFrom(Path.Combine(AppContext.BaseDirectory, "Assets", "Templates"));
        var pipeline = new VisionPipeline(new WindowsNumberReader(), _templates, calibration);

        _overlay = new OverlayWindow();
        _debug = new DebugWindow(data, calibration);
        MainWindow = _debug;
        _debug.Show();

        _loop = new AdvisorLoop(_settings, data, pipeline);
        _loop.SnapshotPublished += snapshot => Dispatcher.BeginInvoke(() =>
        {
            _overlay?.UpdateSnapshot(snapshot);
            _debug?.UpdateSnapshot(snapshot);
        });
        _loop.Start();

        RegisterHotkeys();
    }

    void RegisterHotkeys()
    {
        _hotkeys = new HotkeyManager();
        RegisterToggle(_settings!.ToggleOverlayHotkey, ToggleOverlay);
        RegisterToggle(_settings.ToggleDebugHotkey, ToggleDebug);
    }

    void RegisterToggle(string hotkeyText, Action action)
    {
        var binding = HotkeyBinding.Parse(hotkeyText);
        if (binding is not null)
        {
            _hotkeys!.Register(binding, action);
        }
    }

    void ToggleOverlay()
    {
        if (_overlay is null)
        {
            return;
        }
        if (_overlay.IsVisible)
        {
            _overlay.Hide();
        }
        else
        {
            _overlay.Show();
        }
    }

    void ToggleDebug()
    {
        if (_debug is null)
        {
            return;
        }
        if (_debug.IsVisible)
        {
            _debug.Hide();
        }
        else
        {
            _debug.Show();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeys?.Dispose();
        _loop?.Dispose();
        _templates?.Dispose();
        base.OnExit(e);
    }
}
