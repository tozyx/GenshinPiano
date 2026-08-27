using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;

namespace GenshinPiano.App.Services;

public sealed record SingleInstanceRequest(string? FilePath, bool IsElevated);

public sealed class SingleInstanceCoordinator : IDisposable
{
    private const string MutexName = @"Local\GenshinPiano.v3.SingleInstance";
    private const string PipeName = "GenshinPiano.v3.SingleInstance";
    private const string ActivationMessageName = "GenshinPiano.v3.Activate";
    private const int ShowWindowRestore = 9;
    private const uint FlashAll = 0x00000003;
    private const uint FlashTimerNoForeground = 0x0000000C;
    private static readonly IntPtr BroadcastWindow = new(0xFFFF);
    private static uint _activationMessage;
    private Mutex? _mutex;
    private readonly CancellationTokenSource _listenerCancellation = new();
    private bool _ownsMutex;
    private Task? _listenerTask;

    public static uint ActivationMessage =>
        _activationMessage != 0
            ? _activationMessage
            : _activationMessage = RegisterWindowMessage(ActivationMessageName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(
        Microsoft.Win32.SafeHandles.SafePipeHandle pipe,
        out uint serverProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(
        IntPtr windowHandle,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr windowHandle, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FlashWindowInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashWindowInfo
    {
        public uint Size;
        public IntPtr Window;
        public uint Flags;
        public uint Count;
        public uint Timeout;
    }

    public bool TryAcquire()
    {
        try
        {
            _mutex = new Mutex(false, MutexName);
            _ownsMutex = _mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            _ownsMutex = true;
        }
        catch (UnauthorizedAccessException)
        {
            // A higher-integrity instance can own the same per-session mutex.
            // Treat it as the primary instance and attempt pipe forwarding so the
            // caller can show a clear message if the integrity boundary blocks IPC.
            _ownsMutex = false;
        }
        return _ownsMutex;
    }

    public bool Forward(SingleInstanceRequest request)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(
                ".",
                PipeName,
                PipeDirection.Out);
            pipe.Connect(700);
            try
            {
                if (GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var serverProcessId))
                    AllowSetForegroundWindow(serverProcessId);
            }
            catch (Exception exception) when (
                exception is EntryPointNotFoundException or DllNotFoundException)
            {
                // Foreground permission is an enhancement only. The activation request
                // must still be delivered on systems where the helper API is unavailable.
            }

            var encodedPath = Convert.ToBase64String(
                Encoding.UTF8.GetBytes(request.FilePath ?? string.Empty));
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true);
            writer.WriteLine($"{(request.IsElevated ? '1' : '0')}\t{encodedPath}");
            writer.Flush();
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or TimeoutException or OperationCanceledException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static bool SignalActivationRequest()
    {
        var message = ActivationMessage;
        return message != 0 && PostMessage(BroadcastWindow, message, IntPtr.Zero, IntPtr.Zero);
    }

    public static bool TryActivateExistingInstance()
    {
        using var current = Process.GetCurrentProcess();
        foreach (var process in Process.GetProcessesByName(current.ProcessName)
                     .Where(process => process.Id != current.Id)
                     .OrderBy(process => TryGetStartTime(process)))
        {
            using (process)
            {
                try
                {
                    var handle = process.MainWindowHandle;
                    if (handle == IntPtr.Zero)
                    {
                        continue;
                    }

                    AllowSetForegroundWindow((uint)process.Id);

                    ShowWindow(handle, ShowWindowRestore);
                    if (!SetForegroundWindow(handle))
                    {
                        FlashTaskbar(handle);
                    }
                    return true;
                }
                catch (Exception exception) when (
                    exception is InvalidOperationException or
                    System.ComponentModel.Win32Exception or
                    UnauthorizedAccessException or
                    EntryPointNotFoundException or
                    DllNotFoundException)
                {
                }
            }
        }

        return false;
    }

    private static DateTime TryGetStartTime(Process process)
    {
        try
        {
            return process.StartTime;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return DateTime.MaxValue;
        }
    }

    private static void FlashTaskbar(IntPtr handle)
    {
        var flash = new FlashWindowInfo
        {
            Size = (uint)Marshal.SizeOf<FlashWindowInfo>(),
            Window = handle,
            Flags = FlashAll | FlashTimerNoForeground,
            Count = 3,
        };
        FlashWindowEx(ref flash);
    }

    public void StartListening(Action<SingleInstanceRequest> requestReceived)
    {
        ArgumentNullException.ThrowIfNull(requestReceived);
        if (!_ownsMutex || _listenerTask is not null) return;
        _listenerTask = ListenAsync(requestReceived, _listenerCancellation.Token);
    }

    private static async Task ListenAsync(
        Action<SingleInstanceRequest> requestReceived,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                var line = await reader.ReadLineAsync(cancellationToken);
                if (TryParseRequest(line, out var request))
                {
                    AppLogger.Info(
                        $"Received second-instance request; path='{request.FilePath ?? "<none>"}', " +
                        $"elevated={request.IsElevated}.");
                    requestReceived(request);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                AppLogger.Warning($"Single-instance pipe request failed: {exception.Message}");
            }
        }
    }

    private static bool TryParseRequest(
        string? line,
        out SingleInstanceRequest request)
    {
        request = new SingleInstanceRequest(null, false);
        if (string.IsNullOrEmpty(line)) return false;
        var separator = line.IndexOf('\t');
        if (separator != 1 || line[0] is not ('0' or '1')) return false;
        try
        {
            var path = Encoding.UTF8.GetString(
                Convert.FromBase64String(line[(separator + 1)..]));
            request = new SingleInstanceRequest(
                path.Length == 0 ? null : path,
                line[0] == '1');
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _listenerCancellation.Cancel();
        if (_ownsMutex && _mutex is not null)
        {
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
        }
        _mutex?.Dispose();
        _listenerCancellation.Dispose();
    }
}
