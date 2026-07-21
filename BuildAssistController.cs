using System;
using System.Drawing;
using System.Windows.Forms;

namespace SquadTools;

internal sealed class BuildAssistController : IDisposable
{
    private const int RequiredHoldSeconds = 3;
    private const int ReleaseConfirmationTicks = 3;
    private const double MovementThresholdPixels = 2.0;

    private readonly System.Windows.Forms.Timer monitorTimer = new() { Interval = 100 };
    private readonly OverlayForm overlayForm = new();
    private Point lastMousePosition;
    private DateTime? holdStartedAt;
    private MouseButton? activeButton;
    private MouseButton? buttonBeingReleased;
    private int releaseConfirmationTicksRemaining;
    private bool syntheticButtonDown;
    private AssistState state = AssistState.Off;

    internal event Action<string>? StatusChanged;
    internal event Action<string>? Error;

    internal bool Enabled => monitorTimer.Enabled;

    internal BuildAssistController()
    {
        monitorTimer.Tick += (_, _) => MonitorMouse();
    }

    internal void SetEnabled(bool enabled)
    {
        ReleaseSyntheticButton();
        ResetTracking();

        if (enabled)
        {
            SetState(AssistState.Ready, "准备建造");
            overlayForm.ShowStatus("准备建造");
            monitorTimer.Start();
            return;
        }

        monitorTimer.Stop();
        overlayForm.Hide();
        SetState(AssistState.Off, "未启用");
    }

    private void MonitorMouse()
    {
        Point currentPosition = Cursor.Position;
        bool leftButtonDown = IsPhysicalButtonDown(MouseButton.Left);
        bool rightButtonDown = IsPhysicalButtonDown(MouseButton.Right);
        bool moved = Distance(currentPosition, lastMousePosition) > MovementThresholdPixels;

        if (buttonBeingReleased is MouseButton releasingButton)
        {
            bool buttonStillDown = IsButtonDown(releasingButton, leftButtonDown, rightButtonDown);
            if (!NativeMethods.SendMouse(GetUpFlag(releasingButton)))
            {
                StopAfterInputFailure();
                return;
            }

            if (releaseConfirmationTicksRemaining > 0)
            {
                releaseConfirmationTicksRemaining--;
            }

            if (!buttonStillDown && releaseConfirmationTicksRemaining == 0)
            {
                buttonBeingReleased = null;
            }

            ResetTracking(currentPosition);
            SetState(AssistState.Ready, "准备建造");
            overlayForm.ShowStatus("准备建造");
            return;
        }

        if (state == AssistState.Holding)
        {
            if (moved)
            {
                if (!StartReleaseConfirmation())
                {
                    StopAfterInputFailure();
                    return;
                }

                ResetTracking(currentPosition);
                SetState(AssistState.Ready, "准备建造");
                overlayForm.ShowStatus("准备建造");
                return;
            }

            if (activeButton is not MouseButton holdingButton)
            {
                ResetTracking(currentPosition);
                SetState(AssistState.Ready, "准备建造");
                overlayForm.ShowStatus("准备建造");
                return;
            }

            bool physicalButtonDown = IsButtonDown(holdingButton, leftButtonDown, rightButtonDown);
            if (!physicalButtonDown && !NativeMethods.SendMouse(GetDownFlag(holdingButton)))
            {
                StopAfterInputFailure();
            }

            return;
        }

        if (activeButton is null)
        {
            activeButton = leftButtonDown
                ? MouseButton.Left
                : rightButtonDown
                    ? MouseButton.Right
                    : null;
        }

        if (activeButton is not MouseButton button || !IsButtonDown(button, leftButtonDown, rightButtonDown))
        {
            ResetTracking(currentPosition);
            SetState(AssistState.Ready, "准备建造");
            overlayForm.ShowStatus("准备建造");
            return;
        }

        if (holdStartedAt is null || moved)
        {
            holdStartedAt = DateTime.UtcNow;
            lastMousePosition = currentPosition;
        }

        TimeSpan heldFor = DateTime.UtcNow - holdStartedAt.GetValueOrDefault(DateTime.UtcNow);
        int remaining = RequiredHoldSeconds - (int)Math.Floor(heldFor.TotalSeconds);
        if (remaining > 0)
        {
            SetState(AssistState.Countdown, $"倒计时 {remaining}");
            overlayForm.ShowStatus(remaining.ToString());
            return;
        }

        if (NativeMethods.SendMouse(GetDownFlag(button)))
        {
            syntheticButtonDown = true;
            lastMousePosition = currentPosition;
            string holdingText = button == MouseButton.Right ? "刨除中" : "建造中";
            SetState(AssistState.Holding, holdingText);
            overlayForm.ShowStatus(holdingText);
            return;
        }

        StopAfterInputFailure();
    }

    private bool StartReleaseConfirmation()
    {
        if (!syntheticButtonDown || activeButton is not MouseButton button)
        {
            return false;
        }

        if (!NativeMethods.SendMouse(GetUpFlag(button)))
        {
            return false;
        }

        syntheticButtonDown = false;
        buttonBeingReleased = button;
        releaseConfirmationTicksRemaining = ReleaseConfirmationTicks;
        return true;
    }

    private void ReleaseSyntheticButton()
    {
        MouseButton? button = syntheticButtonDown ? activeButton : buttonBeingReleased;
        if (button is not MouseButton releaseButton)
        {
            return;
        }

        NativeMethods.SendMouse(GetUpFlag(releaseButton));
        syntheticButtonDown = false;
        buttonBeingReleased = null;
        releaseConfirmationTicksRemaining = 0;
    }

    private void StopAfterInputFailure()
    {
        ReleaseSyntheticButton();
        monitorTimer.Stop();
        overlayForm.Hide();
        SetState(AssistState.Off, "未启用");
        Error?.Invoke("Windows 未接受鼠标控制输入，请以与目标程序相同的权限运行本程序。");
    }

    private void ResetTracking()
    {
        ResetTracking(Cursor.Position);
    }

    private void ResetTracking(Point position)
    {
        holdStartedAt = null;
        activeButton = null;
        lastMousePosition = position;
    }

    private void SetState(AssistState nextState, string text)
    {
        if (state == nextState)
        {
            return;
        }

        state = nextState;
        StatusChanged?.Invoke(text);
    }

    private static bool IsPhysicalButtonDown(MouseButton button)
    {
        int virtualKey = button == MouseButton.Right ? 0x02 : 0x01;
        return (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    private static bool IsButtonDown(MouseButton button, bool leftButtonDown, bool rightButtonDown)
    {
        return button == MouseButton.Right ? rightButtonDown : leftButtonDown;
    }

    private static NativeMethods.MouseInputFlags GetDownFlag(MouseButton button)
    {
        return button == MouseButton.Right
            ? NativeMethods.MouseInputFlags.RightDown
            : NativeMethods.MouseInputFlags.LeftDown;
    }

    private static NativeMethods.MouseInputFlags GetUpFlag(MouseButton button)
    {
        return button == MouseButton.Right
            ? NativeMethods.MouseInputFlags.RightUp
            : NativeMethods.MouseInputFlags.LeftUp;
    }

    private static double Distance(Point a, Point b)
    {
        int dx = a.X - b.X;
        int dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public void Dispose()
    {
        ReleaseSyntheticButton();
        monitorTimer.Stop();
        monitorTimer.Dispose();
        overlayForm.Dispose();
    }

    private enum MouseButton
    {
        Left,
        Right
    }

    private enum AssistState
    {
        Off,
        Ready,
        Countdown,
        Holding
    }
}
