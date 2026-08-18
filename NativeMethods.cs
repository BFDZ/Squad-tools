using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

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

    [ThreadStatic]
    private static int lastInputError;

    internal static int LastInputError => lastInputError;

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

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

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

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(
        IntPtr windowHandle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    internal delegate IntPtr LowLevelKeyboardProc(int code, IntPtr message, IntPtr data);

    internal static bool IsSquadForeground()
    {
        IntPtr foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero)
        {
            return false;
        }

        GetWindowThreadProcessId(foregroundWindow, out uint processId);
        if (processId != 0)
        {
            try
            {
                using Process process = Process.GetProcessById((int)processId);
                string processName = process.ProcessName;
                if (processName.StartsWith("SquadGame", StringComparison.OrdinalIgnoreCase) ||
                    processName.Equals("Squad", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (Win32Exception)
            {
                return false;
            }
        }

        return false;
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

        return SendInputs(inputs);
    }

    internal static bool SendKey(ushort virtualKey, bool keyUp = false)
    {
        Input[] inputs =
        [
            CreateKeyboardInput(virtualKey, keyUp)
        ];

        return SendInputs(inputs);
    }

    internal static bool SendKeyPress(ushort virtualKey)
    {
        return SendInputs(
        [
            CreateKeyboardInput(virtualKey, false),
            CreateKeyboardInput(virtualKey, true)
        ]);
    }

    internal static bool SendChord(ushort modifier, ushort virtualKey)
    {
        return SendInputs(
        [
            CreateKeyboardInput(modifier, false),
            CreateKeyboardInput(virtualKey, false),
            CreateKeyboardInput(virtualKey, true),
            CreateKeyboardInput(modifier, true)
        ]);
    }

    internal static string DescribeLastInputError()
    {
        return lastInputError == 0
            ? "Windows 未提供具体错误，游戏也可能过滤了合成输入"
            : $"Win32 错误 {lastInputError}：{new Win32Exception(lastInputError).Message}";
    }

    internal static void KeepWindowTopMost(IntPtr windowHandle)
    {
        const uint noMove = 0x0002;
        const uint noSize = 0x0001;
        const uint noActivate = 0x0010;
        SetWindowPos(windowHandle, new IntPtr(-1), 0, 0, 0, 0, noMove | noSize | noActivate);
    }

    private static bool SendInputs(Input[] inputs)
    {
        lastInputError = 0;
        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent == (uint)inputs.Length)
        {
            return true;
        }

        lastInputError = Marshal.GetLastWin32Error();
        return false;
    }

    private static Input CreateKeyboardInput(ushort virtualKey, bool keyUp)
    {
        return new Input
        {
            Type = InputType.Keyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = 0,
                    ScanCode = (ushort)MapVirtualKey(virtualKey, MapVkToScanCode),
                    Flags = KeyboardInputFlags.ScanCode |
                        (keyUp ? KeyboardInputFlags.KeyUp : KeyboardInputFlags.None)
                }
            }
        };
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
        KeyUp = 0x0002,
        ScanCode = 0x0008
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
