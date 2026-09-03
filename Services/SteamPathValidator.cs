namespace SteamCNGameLaunchAssistant.Services;

/// <summary>Windows relative paths used inside steamapps/common, shared by lookup and generation.</summary>
public static class SteamPathValidator
{
    public static bool TryValidate(string value, out string error, bool requireExe = false)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value) || value.StartsWith('/') || value.StartsWith('\\'))
            error = "路径必须是安装目录内的非空相对路径";
        else
        {
            foreach (var segment in value.Split('/', '\\'))
            {
                var stem = segment.Split('.')[0];
                var reserved = new[] { "CON", "PRN", "AUX", "NUL" }.Contains(stem, StringComparer.OrdinalIgnoreCase)
                    || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                        || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && "123456789¹²³".Contains(stem[3]));
                if (segment.Length == 0 || segment is "." or ".." || segment.EndsWith('.') || segment.EndsWith(' ')
                    || segment.Any(c => c < 32 || "<>:\"|?*".Contains(c)) || reserved)
                {
                    error = "路径包含非法片段、保留名称或目录穿越";
                    break;
                }
            }
        }
        if (error.Length == 0 && requireExe && !value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            error = "当前仅支持 EXE 启动文件";
        return error.Length == 0;
    }

    public static string Normalize(string value) => value.Replace('/', '\\');
}
