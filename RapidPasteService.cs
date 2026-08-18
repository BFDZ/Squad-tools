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
                    if (!NativeMethods.SendKeyPress(NativeMethods.VkOem3))
                    {
                        ReportInputError();
                        return;
                    }

                    await Task.Delay(10, cancellationToken);
                    if (!NativeMethods.IsSquadForeground())
                    {
                        continue;
                    }

                    if (!NativeMethods.SendChord(NativeMethods.VkControl, NativeMethods.VkV))
                    {
                        ReportInputError();
                        return;
                    }

                    await Task.Delay(10, cancellationToken);
                    if (!NativeMethods.IsSquadForeground())
                    {
                        continue;
                    }

                    if (!NativeMethods.SendKeyPress(NativeMethods.VkReturn))
                    {
                        ReportInputError();
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

    private void ReportInputError()
    {
        Error?.Invoke($"Windows 未接受键盘控制输入。{NativeMethods.DescribeLastInputError()}。请确认本程序与游戏权限一致。");
    }

    public void Dispose()
    {
        Stop();
    }
}
