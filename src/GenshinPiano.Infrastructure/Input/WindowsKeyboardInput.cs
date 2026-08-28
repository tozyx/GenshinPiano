using System.ComponentModel;
using System.Runtime.InteropServices;
using GenshinPiano.Application.Abstractions;
using GenshinPiano.Core.Playback;

namespace GenshinPiano.Infrastructure.Input;

public sealed class WindowsKeyboardInput : IKeyboardInput, IKeyboardSafetyController
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventScanCode = 0x0008;
    private const uint KeyEventKeyUp = 0x0002;

    private readonly object _sync = new();
    private readonly HashSet<GenshinKey> _pressedKeys = [];

    public void KeyDown(IReadOnlyList<GenshinKey> keys)
    {
        lock (_sync)
        {
            Send(keys, isKeyUp: false);
            _pressedKeys.UnionWith(keys);
        }
    }

    public void KeyUp(IReadOnlyList<GenshinKey> keys)
    {
        lock (_sync)
        {
            var reversed = keys.Reverse().ToArray();
            Send(reversed, isKeyUp: true);
            _pressedKeys.ExceptWith(keys);
        }
    }

    public void ReleasePressedKeys()
    {
        lock (_sync)
        {
            if (_pressedKeys.Count == 0)
            {
                return;
            }

            var keys = _pressedKeys.OrderByDescending(key => key).ToArray();
            try
            {
                Send(keys, isKeyUp: true);
            }
            finally
            {
                _pressedKeys.Clear();
            }
        }
    }

    public void EmergencyReleaseAllKeys()
    {
        lock (_sync)
        {
            try
            {
                Send(Enum.GetValues<GenshinKey>().Reverse().ToArray(), isKeyUp: true);
            }
            finally
            {
                _pressedKeys.Clear();
            }
        }
    }

    private static void Send(IReadOnlyList<GenshinKey> keys, bool isKeyUp)
    {
        if (keys.Count == 0)
        {
            return;
        }

        var inputs = keys.Select(key => new Input
        {
            Type = InputKeyboard,
            Union = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    ScanCode = GetScanCode(key),
                    Flags = KeyEventScanCode | (isKeyUp ? KeyEventKeyUp : 0),
                },
            },
        }).ToArray();

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows failed to inject keyboard input.");
        }
    }

    private static ushort GetScanCode(GenshinKey key) => key switch
    {
        GenshinKey.Q => 0x10,
        GenshinKey.W => 0x11,
        GenshinKey.E => 0x12,
        GenshinKey.R => 0x13,
        GenshinKey.T => 0x14,
        GenshinKey.Y => 0x15,
        GenshinKey.U => 0x16,
        GenshinKey.A => 0x1E,
        GenshinKey.S => 0x1F,
        GenshinKey.D => 0x20,
        GenshinKey.F => 0x21,
        GenshinKey.G => 0x22,
        GenshinKey.H => 0x23,
        GenshinKey.J => 0x24,
        GenshinKey.Z => 0x2C,
        GenshinKey.X => 0x2D,
        GenshinKey.C => 0x2E,
        GenshinKey.V => 0x2F,
        GenshinKey.B => 0x30,
        GenshinKey.N => 0x31,
        GenshinKey.M => 0x32,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
    };

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput Mouse;

        [FieldOffset(0)]
        public KeyboardInput Keyboard;
    }

    // INPUT is a union whose native size is determined by its largest member.
    // Keeping MOUSEINPUT here is required even though this adapter only sends keys.
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }
}
