using System.Runtime.InteropServices;

namespace ReplayFoundry.Desktop.Platform.Updates;

// Delegates are retained by WinSparkleUpdateService for the native library's lifetime.
internal static class WinSparkleNative
{
    private const string Library = "WinSparkle.dll";
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void Notification();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int CanShutdown();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int RunInstaller([MarshalAs(UnmanagedType.LPWStr)] string path);

    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void win_sparkle_init();
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void win_sparkle_cleanup();
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void win_sparkle_check_update_with_ui();
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void win_sparkle_set_automatic_check_for_updates(int state);
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern int win_sparkle_get_automatic_check_for_updates();
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void win_sparkle_set_update_check_interval(int seconds);
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, BestFitMapping = false, ThrowOnUnmappableChar = true)] internal static extern void win_sparkle_set_appcast_url([MarshalAs(UnmanagedType.LPUTF8Str)] string url);
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, BestFitMapping = false, ThrowOnUnmappableChar = true)] internal static extern int win_sparkle_set_eddsa_public_key([MarshalAs(UnmanagedType.LPUTF8Str)] string key);
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, BestFitMapping = false, ThrowOnUnmappableChar = true)] internal static extern void win_sparkle_set_registry_path([MarshalAs(UnmanagedType.LPUTF8Str)] string path);
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)] internal static extern void win_sparkle_set_app_details(string company, string name, string version);
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)] internal static extern void win_sparkle_set_app_build_version(string build);
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void win_sparkle_set_error_callback(Notification callback);
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void win_sparkle_set_did_find_update_callback(Notification callback);
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void win_sparkle_set_did_not_find_update_callback(Notification callback);
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void win_sparkle_set_can_shutdown_callback(CanShutdown callback);
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void win_sparkle_set_shutdown_request_callback(Notification callback);
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    [DllImport(Library, CallingConvention = CallingConvention.Cdecl)] internal static extern void win_sparkle_set_user_run_installer_callback(RunInstaller callback);
}
