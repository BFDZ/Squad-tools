using System.Text.Json;

namespace SquadTools;

internal enum ProxyType
{
    Http,
    Socks5
}

internal sealed class ProxySettings
{
    internal bool Enabled { get; set; }
    internal ProxyType Type { get; set; } = ProxyType.Http;
    internal string Host { get; set; } = string.Empty;
    internal int Port { get; set; } = 8080;

    internal string CommandLineValue =>
        $"{(Type == ProxyType.Socks5 ? "socks5" : "http")}://{Host}:{Port}";
}

internal static class ProxySettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SquadTools",
        "settings.json");

    internal static ProxySettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                return JsonSerializer.Deserialize<ProxySettings>(File.ReadAllText(SettingsPath), JsonOptions)
                    ?? new ProxySettings();
            }
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
        }

        return new ProxySettings();
    }

    internal static bool Save(ProxySettings settings, out string error)
    {
        try
        {
            string? directory = Path.GetDirectoryName(SettingsPath);
            if (directory is null)
            {
                error = "无法确定配置目录。";
                return false;
            }

            Directory.CreateDirectory(directory);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
            error = string.Empty;
            return true;
        }
        catch (IOException exception)
        {
            error = $"保存配置失败：{exception.Message}";
            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            error = $"没有权限保存配置：{exception.Message}";
            return false;
        }
    }
}
