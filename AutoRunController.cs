using System;
using System.Runtime.InteropServices;

namespace SquadTools;

internal sealed class AutoRunController : IDisposable
{
    private readonly System.Windows.Forms.Timer inputTimer = new() { Interval = 50 };
    private readonly NativeMethods.LowLevelKeyboardProc keyboardCallback;
    private IntPtr keyboardHook;
    private bool wDown;
    private bool leftShiftDown;
    private bool hasStartedInSquad;

    internal event Action<string>? StatusChanged;
    internal event Action<string>? Error;
    internal event Action<string>? Stopped;

    internal bool Enabled { get; private set; }

    internal AutoRunController()
    {
        keyboardCallback = HandleKeyboardInput;
        inputTimer.Tick += (_, _) => MaintainKeys();
    }

    internal void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            Stop("未启用", false);
            return;
        }

        if (!InstallKeyboardHook())
        {
            Error?.Invoke("无法监听键盘输入，请尝试以管理员身份运行本程序。");
            return;
        }

        Enabled = true;
        hasStartedInSquad = false;
        StatusChanged?.Invoke("已开启，等待 Squad 窗口");
        inputTimer.Start();
        MaintainKeys();
    }

    private void MaintainKeys()
    {
        if (!Enabled)
        {
            return;
        }

        bool squadForeground = NativeMethods.IsSquadForeground();
        if (!squadForeground)
        {
            if (hasStartedInSquad)
            {
                Stop("已停止：已切出 Squad", true);
            }

            return;
        }

        if (!wDown)
        {
            if (!NativeMethods.SendKey(NativeMethods.VkW))
            {
                Stop("未启用", true);
                Error?.Invoke($"Windows 未接受自动奔跑按键输入。{NativeMethods.DescribeLastInputError()}。请确认本程序与游戏权限一致。");
                return;
            }

            wDown = true;
            hasStartedInSquad = true;
            StatusChanged?.Invoke("自动奔跑中");
            return;
        }

        if (!leftShiftDown)
        {
            if (!NativeMethods.SendKey(NativeMethods.VkLeftShift))
            {
                Stop("未启用", true);
                Error?.Invoke($"Windows 未接受自动奔跑按键输入。{NativeMethods.DescribeLastInputError()}。请确认本程序与游戏权限一致。");
                return;
            }

            leftShiftDown = true;
        }
    }

    private IntPtr HandleKeyboardInput(int code, IntPtr message, IntPtr data)
    {
        if (Enabled && code >= 0 &&
            (message.ToInt32() == NativeMethods.WmKeyDown || message.ToInt32() == NativeMethods.WmSystemKeyDown))
        {
            NativeMethods.LowLevelKeyboardInput input = Marshal.PtrToStructure<NativeMethods.LowLevelKeyboardInput>(data);
            bool injected = (input.Flags & NativeMethods.LowLevelKeyboardFlags.Injected) != 0;
            if (!injected && input.VirtualKey != NativeMethods.VkF10)
            {
                Stop("已停止：检测到键盘输入", true);
            }
        }

        return NativeMethods.CallNextHookEx(keyboardHook, code, message, data);
    }

    private bool InstallKeyboardHook()
    {
        if (keyboardHook != IntPtr.Zero)
        {
            return true;
        }

        keyboardHook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhKeyboardLowLevel,
            keyboardCallback,
            NativeMethods.GetModuleHandle(null),
            0);
        return keyboardHook != IntPtr.Zero;
    }

    private void RemoveKeyboardHook()
    {
        if (keyboardHook == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.UnhookWindowsHookEx(keyboardHook);
        keyboardHook = IntPtr.Zero;
    }

    private void Stop(string status, bool notify)
    {
        bool wasEnabled = Enabled;
        inputTimer.Stop();
        Enabled = false;
        ReleaseKeys();
        RemoveKeyboardHook();
        hasStartedInSquad = false;
        StatusChanged?.Invoke(status);
        if (notify && wasEnabled)
        {
            Stopped?.Invoke(status);
        }
    }

    private void ReleaseKeys()
    {
        if (leftShiftDown)
        {
            NativeMethods.SendKey(NativeMethods.VkLeftShift, true);
            leftShiftDown = false;
        }

        if (wDown)
        {
            NativeMethods.SendKey(NativeMethods.VkW, true);
            wDown = false;
        }
    }

    public void Dispose()
    {
        Stop("未启用", false);
        inputTimer.Dispose();
    }
}
