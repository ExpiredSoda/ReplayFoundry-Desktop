using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using ReplayFoundry.Desktop.Features.Settings;
using ReplayFoundry.Desktop.Platform.Diagnostics;

namespace ReplayFoundry.Desktop.Platform.Updates;

internal sealed class WinSparkleUpdateService : IApplicationUpdateService, IDisposable
{
    private readonly Func<string?> _blockReason;
    private readonly WinSparkleNative.Notification _error, _found, _current, _shutdown;
    private readonly WinSparkleNative.CanShutdown _canShutdown;
    private readonly WinSparkleNative.RunInstaller _runInstaller;
    private Window? _window;
    private Dispatcher? _dispatcher;
    private bool _initialized, _disposed, _installing;
    private bool _automaticChecks;
    private IntPtr _library;
    private string _status = "Update checks are available in installed release builds.";

    public WinSparkleUpdateService(Func<string?> blockReason)
    {
        _blockReason = blockReason;
        _error = () => Post(() => SetStatus("Could not check or download this update. Check your connection and try again."));
        _found = () => Post(() => SetStatus("An update is available. Review the update window to download it."));
        _current = () => Post(() => SetStatus("You're up to date."));
        _shutdown = () => Post(() => _window?.Close());
        _canShutdown = CanShutdown;
        _runInstaller = RunInstaller;
    }

    public event EventHandler? Changed;
    public bool IsAvailable => _initialized && !_disposed;
    public string Status => _status;
    public bool AutomaticChecksEnabled
    {
        get => _automaticChecks;
        set
        {
            if (!IsAvailable || _automaticChecks == value) return;
            WinSparkleNative.win_sparkle_set_automatic_check_for_updates(value ? 1 : 0);
            _automaticChecks = value;
            SetStatus(value ? "Automatic update checks are on. You choose when to install." : "Automatic checks are off. You can still check here.");
        }
    }

    public void Initialize(Window window, ApplicationUpdateConfiguration? qualificationConfiguration = null)
    {
        if (_initialized || _disposed) return;
        _window = window;
        _dispatcher = window.Dispatcher;
        try
        {
            ApplicationUpdateConfiguration? configuration = qualificationConfiguration ?? ApplicationUpdateConfiguration.FromAssembly();
            if (configuration is null) return;
            // Load only the installed, packaged DLL, never a current-directory DLL.
            _library = NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "WinSparkle.dll"));
            WinSparkleNative.win_sparkle_set_app_details("Expired Soda Studios LLC", "Replay Foundry", configuration.DisplayVersion);
            WinSparkleNative.win_sparkle_set_app_build_version(configuration.BuildVersion);
            WinSparkleNative.win_sparkle_set_appcast_url(configuration.FeedUrl);
            if (WinSparkleNative.win_sparkle_set_eddsa_public_key(configuration.PublicKey) != 1)
                throw new InvalidOperationException("The update verification key was rejected.");
            WinSparkleNative.win_sparkle_set_registry_path($@"Software\Expired Soda Studios LLC\Replay Foundry\Updates\{configuration.Channel}");
            // Make the first-run choice explicit in Settings; do not show a second consent popup.
            _automaticChecks = WinSparkleNative.win_sparkle_get_automatic_check_for_updates() != 0;
            WinSparkleNative.win_sparkle_set_automatic_check_for_updates(_automaticChecks ? 1 : 0);
            WinSparkleNative.win_sparkle_set_update_check_interval(86400);
            WinSparkleNative.win_sparkle_set_error_callback(_error);
            WinSparkleNative.win_sparkle_set_did_find_update_callback(_found);
            WinSparkleNative.win_sparkle_set_did_not_find_update_callback(_current);
            WinSparkleNative.win_sparkle_set_can_shutdown_callback(_canShutdown);
            WinSparkleNative.win_sparkle_set_user_run_installer_callback(_runInstaller);
            WinSparkleNative.win_sparkle_set_shutdown_request_callback(_shutdown);
            WinSparkleNative.win_sparkle_init();
            _initialized = true;
            SetStatus($"{(configuration.Channel == "beta" ? "Beta" : "Stable")} updates · build {configuration.BuildVersion}. You choose when to install.");
        }
        catch (Exception exception) when (exception is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException or InvalidOperationException or FormatException)
        {
            SafeDiagnosticTrace.Write("Application updater could not initialize", exception);
            SetStatus("Update checks could not start. Re-run the latest setup to repair the app.");
        }
    }

    public void CheckForUpdates()
    {
        if (!IsAvailable) return;
        SetStatus("Checking for updates…");
        WinSparkleNative.win_sparkle_check_update_with_ui();
    }

    private int CanShutdown()
    {
        try
        {
            return OnUi(() =>
            {
                string? reason = _blockReason();
                if (reason is not null) { SetStatus(reason); return 0; }
                if (_window is not { IsEnabled: true } || Application.Current.Windows.Cast<Window>().Any(window => window != _window && window.IsVisible)) return 0;
                return !_installing && !_disposed ? 1 : 0;
            });
        }
        catch (Exception exception)
        {
            SafeDiagnosticTrace.Write("Update restart was deferred", exception);
            return 0; // Exceptions must never cross a native callback boundary.
        }
    }

    private int RunInstaller(string path)
    {
        try
        {
            return OnUi(() =>
            {
                string? reason = _blockReason();
                if (reason is not null || _disposed || _installing)
                {
                    SetStatus(reason ?? "Finish the current update first.");
                    return -1;
                }
                if (!Path.IsPathFullyQualified(path) || !File.Exists(path) ||
                    !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
                    throw new IOException("The verified update installer is unavailable.");
                // WinSparkle verified EdDSA before this callback. Use our own
                // fixed arguments instead of allowing feed-controlled switches.
                var start = new ProcessStartInfo(path) { UseShellExecute = true };
                foreach (string argument in new[] { "/UPDATE=1", $"/UPDATEPID={Environment.ProcessId}", "/SILENT", "/SP-", "/NORESTART", "/NOCLOSEAPPLICATIONS" })
                    start.ArgumentList.Add(argument);
                _installing = true;
                _window!.IsEnabled = false;
                try
                {
                    using Process process = Process.Start(start) ?? throw new IOException("Windows did not start the installer.");
                    SetStatus("Restarting to install the update…");
                    return 1;
                }
                catch
                {
                    _installing = false;
                    _window.IsEnabled = true;
                    throw;
                }
            }, unavailable: -1);
        }
        catch (Exception exception)
        {
            SafeDiagnosticTrace.Write("Application update could not start", exception);
            Post(() => SetStatus("The update could not start. Your current installation is unchanged; try again."));
            return -1;
        }
    }

    private int OnUi(Func<int> action, int unavailable = 0) => _dispatcher is { HasShutdownStarted: false } dispatcher
        ? dispatcher.Invoke(action, DispatcherPriority.Send, CancellationToken.None, TimeSpan.FromSeconds(5)) : unavailable;
    private void Post(Action action)
    {
        if (!_disposed && _dispatcher is { HasShutdownStarted: false } dispatcher)
            dispatcher.BeginInvoke(() => { if (!_disposed) action(); });
    }
    private void SetStatus(string status) { _status = status; Changed?.Invoke(this, EventArgs.Empty); }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_initialized) WinSparkleNative.win_sparkle_cleanup();
        _initialized = false;
        if (_library != IntPtr.Zero) { NativeLibrary.Free(_library); _library = IntPtr.Zero; }
        GC.KeepAlive(_error); GC.KeepAlive(_found); GC.KeepAlive(_current);
        GC.KeepAlive(_shutdown); GC.KeepAlive(_canShutdown); GC.KeepAlive(_runInstaller);
    }
}
