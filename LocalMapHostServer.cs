using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SquadTools;

internal sealed class LocalMapHostServer : IDisposable
{
    private readonly string rootDirectory;
    private readonly TcpListener listener;
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task acceptLoop;

    private LocalMapHostServer(string rootDirectory, TcpListener listener)
    {
        this.rootDirectory = Path.GetFullPath(rootDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        this.listener = listener;
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        acceptLoop = Task.Run(AcceptLoopAsync);
    }

    internal int Port { get; }
    internal string BaseUrl => $"http://127.0.0.1:{Port}/";

    internal static LocalMapHostServer Start(string rootDirectory)
    {
        if (!File.Exists(Path.Combine(rootDirectory, "index.html")))
        {
            throw new DirectoryNotFoundException($"本地地图工具资源不存在：{rootDirectory}");
        }

        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        return new LocalMapHostServer(rootDirectory, listener);
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                TcpClient client = await listener.AcceptTcpClientAsync(cancellation.Token);
                _ = HandleClientAsync(client);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        using (NetworkStream stream = client.GetStream())
        {
            try
            {
                byte[] buffer = new byte[16 * 1024];
                int count = await stream.ReadAsync(buffer, cancellation.Token);
                if (count == 0) return;

                string[] requestLine = Encoding.ASCII.GetString(buffer, 0, count)
                    .Split("\r\n", 2, StringSplitOptions.None)[0]
                    .Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                if (requestLine.Length < 2 || !requestLine[0].Equals("GET", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteResponseAsync(stream, 405, "Method Not Allowed", "text/plain", [], cancellation.Token);
                    return;
                }

                string requestPath = requestLine[1].Split('?', 2)[0];
                string relativePath = Uri.UnescapeDataString(requestPath.TrimStart('/'));
                if (relativePath.Length == 0) relativePath = "index.html";

                string fullPath = Path.GetFullPath(Path.Combine(rootDirectory, relativePath));
                if (!fullPath.StartsWith(rootDirectory, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
                {
                    await WriteResponseAsync(stream, 404, "Not Found", "text/plain", [], cancellation.Token);
                    return;
                }

                byte[] content = await File.ReadAllBytesAsync(fullPath, cancellation.Token);
                await WriteResponseAsync(stream, 200, "OK", GetContentType(fullPath), content, cancellation.Token);
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
        }
    }

    private static async Task WriteResponseAsync(NetworkStream stream, int code, string text, string type, byte[] content, CancellationToken token)
    {
        string headers = $"HTTP/1.1 {code} {text}\r\nContent-Type: {type}\r\nContent-Length: {content.Length}\r\nCache-Control: no-cache\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(headers), token);
        if (content.Length > 0) await stream.WriteAsync(content, token);
    }

    private static string GetContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".webp" => "image/webp",
        ".png" => "image/png",
        ".svg" => "image/svg+xml",
        ".mp3" => "audio/mpeg",
        ".ico" => "image/x-icon",
        _ => "application/octet-stream"
    };

    public void Dispose()
    {
        cancellation.Cancel();
        listener.Stop();
        try { acceptLoop.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
        cancellation.Dispose();
    }
}
