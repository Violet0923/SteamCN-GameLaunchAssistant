using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.ApplicationModel.DynamicDependency;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace SteamCNGameLaunchAssistant;

internal static class Program
{
    private static Mutex? _singleInstanceMutex;

    private static readonly string EarlyCrashLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SteamCN-GameLaunchAssistant",
        "logs",
        "early-startup.log");

    private static readonly string EarlyCrashFallbackPath = Path.Combine(
        AppContext.BaseDirectory,
        "early-startup.log");

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_RESTORE = 9;

    [STAThread]
    private static void Main(string[] args)
    {
        WriteEarlyLog("Program.Main.Start", null);

        // Preserve the legacy mutex to prevent old and renamed builds editing settings together.
        _singleInstanceMutex = new Mutex(true, @"Local\WetheringWavesSteamHelper_WinUI_SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            var hwnd = FindWindow(null, AppInfo.WindowTitle);
            if (hwnd != IntPtr.Zero)
            {
                ShowWindow(hwnd, SW_RESTORE);
                SetForegroundWindow(hwnd);
            }
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            return;
        }

#if !WINDOWS_APP_SDK_SELF_CONTAINED
        var bootstrapInitialized = false;
#endif

        try
        {
            WinRT.ComWrappersSupport.InitializeComWrappers();
            WriteEarlyLog("Program.Main.ComWrappers.Initialized", null);

#if !WINDOWS_APP_SDK_SELF_CONTAINED
            var options = Bootstrap.InitializeOptions.OnNoMatch_ShowUI | Bootstrap.InitializeOptions.OnPackageIdentity_NOOP;
            if (Bootstrap.TryInitialize(0x00010008, "", new PackageVersion(), options, out var hr))
            {
                bootstrapInitialized = true;
                WriteEarlyLog("Program.Main.Bootstrap.Initialized", null);
            }
            else
            {
                WriteEarlyLog($"Program.Main.Bootstrap.TryInitialize.False.HResult=0x{hr:X8}", null);
            }

#endif
            // Self-contained builds use the SDK's registration-free WinRT auto-initializer.
            Application.Start(_ =>
            {
                WriteEarlyLog("Application.Start.Callback.Enter", null);

                SynchronizationContext.SetSynchronizationContext(
                    new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));

                new App();
                WriteEarlyLog("Application.Start.Callback.App.Created", null);
            });

            WriteEarlyLog("Program.Main.Application.Start.Returned", null);
        }
        catch (Exception ex)
        {
            WriteEarlyLog("Program.Main.Catch", ex);
            throw;
        }
        finally
        {
#if !WINDOWS_APP_SDK_SELF_CONTAINED
            if (bootstrapInitialized)
            {
                try
                {
                    Bootstrap.Shutdown();
                    WriteEarlyLog("Program.Main.Bootstrap.Shutdown", null);
                }
                catch (Exception ex)
                {
                    WriteEarlyLog("Program.Main.Bootstrap.Shutdown.Catch", ex);
                }
            }
#endif
        }
    }

    private static void WriteEarlyLog(string source, Exception? ex)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("==============================");
            sb.AppendLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            sb.AppendLine($"Source: {source}");
            sb.AppendLine($"PrimaryPath: {EarlyCrashLogPath}");
            sb.AppendLine($"FallbackPath: {EarlyCrashFallbackPath}");

            if (ex != null)
            {
                sb.AppendLine($"Exception: {ex.GetType().FullName}");
                sb.AppendLine($"Message: {ex.Message}");
                sb.AppendLine($"HResult: 0x{ex.HResult:X8}");
                sb.AppendLine("StackTrace:");
                sb.AppendLine(ex.StackTrace ?? "<null>");
            }

            var content = sb.ToString();
            if (!TryAppend(EarlyCrashLogPath, content))
            {
                TryAppend(EarlyCrashFallbackPath, content);
            }
        }
        catch
        {
        }
    }

    private static bool TryAppend(string path, string content)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.AppendAllText(path, content, Encoding.UTF8);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
