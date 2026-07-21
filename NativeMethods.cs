using System;
using System.Runtime.InteropServices;
using System.Text;

namespace SquadTools;

internal static class NativeMethods
{
    internal const int WmHotKey = 0x0312;
    internal const int VkF9 = 0x78;
    internal const int VkOem3 = 0xC0;
    internal const int VkControl = 0x11;
    internal const int VkV = 0x56;
    internal const int VkReturn = 0x0D;

    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(IntPtr windowHandle, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(IntPtr windowHandle, int id);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(IntPtr windowHandle, StringBuilder text, int maxCount);

    internal static bool IsSquadForeground()
    {
        StringBuilder title = new(512);
        GetWindowText(GetForegroundWindow(), title, title.Capacity);
        return title.ToString().Contains("Squad", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool SendMouse(MouseInputFlags flags)
    {
        Input[] inputs =
        [
            new Input
            {
                Type = InputType.Mouse,
                Data = new InputUnion { Mouse = new MouseInput { Flags = flags } }
            }
        ];

        return SendInput(1, inputs, Marshal.SizeOf<Input>()) == 1;
    }

    internal static bool SendKey(ushort virtualKey, bool keyUp = false)
    {
        Input[] inputs =
        [
            new Input
            {
                Type = InputType.Keyboard,
                Data = new InputUnion
                {
                    Keyboard = new KeyboardInput
                    {
                        VirtualKey = virtualKey,
                        Flags = keyUp ? KeyboardInputFlags.KeyUp : KeyboardInputFlags.None
                    }
                }
            }
        ];

        return SendInput(1, inputs, Marshal.SizeOf<Input>()) == 1;
    }

    internal enum InputType : uint
    {
        Mouse = 0,
        Keyboard = 1
    }

    [Flags]
    internal enum MouseInputFlags : uint
    {
        LeftDown = 0x0002,
        LeftUp = 0x0004,
        RightDown = 0x0008,
        RightUp = 0x0010
    }

    [Flags]
    internal enum KeyboardInputFlags : uint
    {
        None = 0,
        KeyUp = 0x0002
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Input
    {
        public InputType Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput Mouse;

        [FieldOffset(0)]
        public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public MouseInputFlags Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public KeyboardInputFlags Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }
}
