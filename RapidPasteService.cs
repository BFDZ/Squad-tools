using System;
using System.Threading;
using System.Threading.Tasks;

namespace SquadTools;

internal sealed class RapidPasteService : IDisposable
{
    private readonly object syncRoot = new();
    private CancellationTokenSource? cancellation;

    internal event Action<string>? Error;

    internal bool IsRunning
    {
        get
        {
            lock (syncRoot)
            {
                return cancellation is { IsCancellationRequested: false };
            }
        }
    }

    internal void Start(int intervalMilliseconds)
    {
        Stop();
        CancellationTokenSource nextCancellation = new();
        lock (syncRoot)
        {
            cancellation = nextCancellation;
        }

        _ = Task.Run(() => RunLoop(intervalMilliseconds, nextCancellation));
    }

    internal void Stop()
    {
        CancellationTokenSource? current;
        lock (syncRoot)
        {
            current = cancellation;
            cancellation = null;
        }

        current?.Cancel();
    }

    private async Task RunLoop(int intervalMilliseconds, CancellationTokenSource source)
    {
        CancellationToken cancellationToken = source.Token;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (NativeMethods.IsSquadForeground())
                {
                    bool accepted = PressKey(NativeMethods.VkOem3);
                    await Task.Delay(10, cancellationToken);
                    accepted &= SendPasteShortcut();
                    await Task.Delay(10, cancellationToken);
                    accepted &= PressKey(NativeMethods.VkReturn);

                    if (!accepted)
                    {
                        Error?.Invoke("Windows 未接受键盘控制输入，请以与游戏相同的权限运行本程序。");
                        return;
                    }
                }

                await Task.Delay(intervalMilliseconds, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Error?.Invoke($"极速粘贴运行失败：{exception.Message}");
        }
        finally
        {
            lock (syncRoot)
            {
                if (ReferenceEquals(cancellation, source))
                {
                    cancellation = null;
                }
            }

            source.Dispose();
        }
    }

    private static bool PressKey(ushort virtualKey)
    {
        bool accepted = NativeMethods.SendKey(virtualKey);
        return NativeMethods.SendKey(virtualKey, true) && accepted;
    }

    private static bool SendPasteShortcut()
    {
        bool accepted = NativeMethods.SendKey(NativeMethods.VkControl);
        accepted &= NativeMethods.SendKey(NativeMethods.VkV);
        accepted &= NativeMethods.SendKey(NativeMethods.VkV, true);
        accepted &= NativeMethods.SendKey(NativeMethods.VkControl, true);
        return accepted;
    }

    public void Dispose()
    {
        Stop();
    }
}
