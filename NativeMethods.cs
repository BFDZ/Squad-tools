using System;
using System.Runtime.InteropServices;
using System.Text;

namespace SquadTools;

internal static class NativeMethods
{
    internal const int WhKeyboardLowLevel = 13;
    internal const int WmKeyDown = 0x0100;
    internal const int WmSystemKeyDown = 0x0104;
    internal const int WmHotKey = 0x0312;
    internal const int VkF8 = 0x77;
    internal const int VkF9 = 0x78;
    internal const int VkF10 = 0x79;
    internal const int VkOem3 = 0xC0;
    internal const int VkControl = 0x11;
    internal const int VkLeftShift = 0xA0;
    internal const int VkW = 0x57;
    internal const int VkV = 0x56;
    internal const int VkReturn = 0x0D;

    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    internal static extern uint MapVirtualKey(uint virtualKey, uint mapType);

    internal const uint MapVkToScanCode = 0;

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

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetWindowsHookEx(
        int hookId,
        LowLevelKeyboardProc callback,
        IntPtr moduleHandle,
        uint threadId);

    [DllImport("user32.dll")]
    internal static extern IntPtr CallNextHookEx(IntPtr hookHandle, int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(IntPtr hookHandle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr GetModuleHandle(string? moduleName);

    internal delegate IntPtr LowLevelKeyboardProc(int code, IntPtr message, IntPtr data);

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
                        ScanCode = (ushort)MapVirtualKey(virtualKey, MapVkToScanCode),
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

    [Flags]
    internal enum LowLevelKeyboardFlags : uint
    {
        None = 0,
        Injected = 0x00000010
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LowLevelKeyboardInput
    {
        public uint VirtualKey;
        public uint ScanCode;
        public LowLevelKeyboardFlags Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }
}
