using System.Reflection;

namespace SquadTools;

/// <summary>
/// 界面显示的版本号统一从程序集元数据读取，发版只需修改 csproj 里的 Version。
/// </summary>
internal static class AppVersion
{
    /// <summary>形如 "1.5.4"；InformationalVersion 若带 "+commit" 后缀会被去掉。</summary>
    internal static string Display { get; } = Resolve();

    /// <summary>形如 "Squad小帮手 v1.5.4"。</summary>
    internal static string WindowTitle => $"Squad小帮手 v{Display}";

    /// <summary>形如 "v1.5.4"。</summary>
    internal static string ShortText => $"v{Display}";

    private static string Resolve()
    {
        Assembly assembly = typeof(AppVersion).Assembly;

        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            int plus = informational.IndexOf('+', StringComparison.Ordinal);
            string trimmed = (plus > 0 ? informational[..plus] : informational).Trim();
            if (trimmed.Length > 0)
            {
                return trimmed;
            }
        }

        Version? version = assembly.GetName().Version;
        return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
