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
    MainWindow? _main;
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
        _main = new MainWindow(data, calibration);
        _main.OverlayToggleRequested += ToggleOverlay;
        _main.GameWindowSelected += OnGameWindowSelected;
        MainWindow = _main;
        _main.Show();

        _loop = new AdvisorLoop(_settings, data, pipeline);
        _loop.SnapshotPublished += snapshot => Dispatcher.BeginInvoke(() =>
        {
            _overlay?.UpdateSnapshot(snapshot);
            _main?.UpdateSnapshot(snapshot);
        });
        _loop.Start();

        RegisterHotkeys();
    }

    void OnGameWindowSelected(string? title)
    {
        _loop?.SetTargetWindow(title);
        _settings = (_settings ?? new AppSettings()) with { WindowTitle = title ?? "" };
        _settings.Save();
    }

    void RegisterHotkeys()
    {
        _hotkeys = new HotkeyManager();
        RegisterToggle(_settings!.ToggleOverlayHotkey, ToggleOverlay);
        RegisterToggle(_settings.ToggleDebugHotkey, ToggleMain);
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
        _main?.SetOverlayVisible(_overlay.IsVisible);
    }

    void ToggleMain()
    {
        if (_main is null)
        {
            return;
        }
        if (_main.IsVisible)
        {
            _main.Hide();
        }
        else
        {
            _main.Show();
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
