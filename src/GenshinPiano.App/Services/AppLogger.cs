using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace GenshinPiano.App.Services;

public static class AppLogger
{
    private static readonly object Sync = new();
    private static readonly string LogDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
    private static readonly string LatestLogPath = Path.Combine(LogDirectory, "latest.log");
    private static bool _available;

    public static void Initialize()
    {
        lock (Sync)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                File.WriteAllText(LatestLogPath, BuildSessionHeader(), new UTF8Encoding(false));
                _available = true;
            }
            catch (IOException)
            {
                _available = false;
            }
            catch (UnauthorizedAccessException)
            {
                _available = false;
            }
        }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warning(string message) => Write("WARN", message);

    public static void Error(string message, Exception exception) =>
        Write("ERROR", $"{message}{Environment.NewLine}{exception}");

    public static void WriteCrashReport(string source, Exception exception)
    {
        lock (Sync)
        {
            if (!_available)
            {
                return;
            }

            try
            {
                WriteUnsafe("FATAL", $"Unhandled exception ({source}){Environment.NewLine}{exception}");
                var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
                var crashPath = Path.Combine(LogDirectory, $"crash-{timestamp}.log");
                var report = File.ReadAllText(LatestLogPath, Encoding.UTF8) +
                             Environment.NewLine +
                             "Crash source: " + source + Environment.NewLine +
                             exception + Environment.NewLine;
                File.WriteAllText(crashPath, report, new UTF8Encoding(false));
            }
            catch (IOException)
            {
                // Logging must never replace the original application failure.
            }
            catch (UnauthorizedAccessException)
            {
                // Continue normal exception handling when the portable folder is read-only.
            }
        }
    }

    private static void Write(string level, string message)
    {
        lock (Sync)
        {
            if (!_available)
            {
                return;
            }

            try
            {
                WriteUnsafe(level, message);
            }
            catch (IOException)
            {
                _available = false;
            }
            catch (UnauthorizedAccessException)
            {
                _available = false;
            }
        }
    }

    private static void WriteUnsafe(string level, string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] [T{Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}";
        File.AppendAllText(LatestLogPath, line, new UTF8Encoding(false));
    }

    private static string BuildSessionHeader()
    {
        var assembly = Assembly.GetEntryAssembly();
        var version = assembly?.GetName().Version?.ToString() ?? "unknown";
        return $"GenshinPiano v{version}{Environment.NewLine}" +
               $"Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff zzz}{Environment.NewLine}" +
               $"OS: {RuntimeInformation.OSDescription}{Environment.NewLine}" +
               $"Runtime: {RuntimeInformation.FrameworkDescription}{Environment.NewLine}" +
               $"Architecture: {RuntimeInformation.ProcessArchitecture}{Environment.NewLine}" +
               $"Process: {Environment.ProcessId} ({Process.GetCurrentProcess().ProcessName}){Environment.NewLine}" +
               new string('-', 72) + Environment.NewLine;
    }
}
