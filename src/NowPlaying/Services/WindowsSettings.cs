using Microsoft.Win32;

namespace NowPlaying.Services;

/// <summary>The Windows settings the app reads or changes: the start with Windows, the theme of the taskbar.</summary>
internal static class WindowsSettings
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "NowPlaying";
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    /// <summary>Whether NowPlaying starts with Windows: the Run key, or the startup task of the Store version.</summary>
    public static Task<StartupState> GetStartupAsync() =>
        AppPackage.IsPackaged ? AppPackage.GetStartupAsync() : Task.FromResult(StartWithWindows ? StartupState.On : StartupState.Off);

    public static Task<StartupState> SetStartupAsync(bool on)
    {
        if (AppPackage.IsPackaged)
            return AppPackage.SetStartupAsync(on);
        StartWithWindows = on;
        return GetStartupAsync();
    }

    /// <summary>
    /// NowPlaying starts, in the notification area, when the user signs in (HKCU Run key). Not in the Store version:
    /// a package writes its own copy of HKCU, which Windows never reads at sign-in.
    /// </summary>
    public static bool StartWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunValue) is string;
        }
        set
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                if (value && Environment.ProcessPath is { } exe)
                    key.SetValue(RunValue, $"\"{exe}\" {App.BackgroundArgument}");
                else
                    key.DeleteValue(RunValue, throwOnMissingValue: false);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
            }
        }
    }

    /// <summary>
    /// True when this exe is the one installed from GitHub (Program Files): only then is the start with Windows turned
    /// on by default. A development build never registers itself; the Store version has its startup task.
    /// </summary>
    public static bool IsInstalled =>
        !AppPackage.IsPackaged
        && Environment.ProcessPath is { } exe
        && exe.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), StringComparison.OrdinalIgnoreCase);

    /// <summary>The taskbar (and Start) is dark: "Choose your mode" of Windows, the Windows part (dark by default).</summary>
    public static bool TaskbarIsDark
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
                return key?.GetValue("SystemUsesLightTheme") is not int light || light == 0;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                return true;
            }
        }
    }
}
