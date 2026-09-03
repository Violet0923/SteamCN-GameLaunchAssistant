using System.Globalization;
using System.Net;
using System.Text.Json;
using WetheringWavesSteamHelper_WinUI.Models;

namespace WetheringWavesSteamHelper_WinUI.Services;

public interface ISteamAppInfoService
{
    Task<SteamAppInfo> GetAppInfoAsync(string appId, CancellationToken cancellationToken = default);
}

public sealed class SteamAppInfoException : Exception
{
    public SteamAppInfoException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>Transport only; HttpClient and endpoint can be replaced without changing parsing or UI.</summary>
public sealed class SteamAppInfoService : ISteamAppInfoService
{
    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly HttpClient _client;
    private readonly Uri _endpoint;

    public SteamAppInfoService(HttpClient? client = null, Uri? endpoint = null)
    {
        _client = client ?? SharedClient;
        _endpoint = endpoint ?? new Uri("https://api.steamcmd.net/v1/info/");
    }

    public static bool TryNormalizeAppId(string value, out string appId)
    {
        appId = "";
        if (!uint.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id == 0)
            return false;
        appId = id.ToString(CultureInfo.InvariantCulture);
        return true;
    }

    public async Task<SteamAppInfo> GetAppInfoAsync(string appId, CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeAppId(appId, out var normalizedId))
            throw new SteamAppInfoException("AppID 必须为 1 到 4294967295 之间的整数。");
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_endpoint, normalizedId));
            request.Headers.UserAgent.ParseAdd("WutheringWavesSteamHelper");
            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new SteamAppInfoException("未找到该 AppID 的公开信息，请检查 AppID。");
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return SteamAppInfoParser.Parse(json, normalizedId);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SteamAppInfoException("获取超时，请稍后重试或手动填写。", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new SteamAppInfoException("无法连接游戏信息服务，请检查网络或稍后重试。", ex);
        }
        catch (JsonException ex)
        {
            throw new SteamAppInfoException("游戏信息服务返回的格式异常，请稍后重试。", ex);
        }
    }
}

/// <summary>Missing optional fields stay empty; identifiers never pass through floating point.</summary>
public static class SteamAppInfoParser
{
    public static SteamAppInfo Parse(string json, string appId)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (Value(root, "status") != "success")
            throw new SteamAppInfoException("服务未返回该 AppID 的有效信息，请检查 AppID 或稍后重试。");
        var app = Child(Child(root, "data"), appId);
        if (app.ValueKind != JsonValueKind.Object || !app.EnumerateObject().Any())
            throw new SteamAppInfoException("该 AppID 暂无可用的公开配置。");

        var common = Child(app, "common");
        var config = Child(app, "config");
        var depots = Child(app, "depots");
        var builds = Properties(Child(depots, "branches")).ToDictionary(
            p => p.Name, p => Value(p.Value, "buildid"), StringComparer.OrdinalIgnoreCase);
        var depotList = Properties(depots)
            .Where(p => SteamAppInfoService.TryNormalizeAppId(p.Name, out _) && p.Value.ValueKind == JsonValueKind.Object)
            .Select(p => new SteamDepotInfo(p.Name, Value(p.Value, "name"), Filter(p.Value), Value(p.Value, "dlcappid"),
                Properties(Child(p.Value, "manifests")).ToDictionary(m => m.Name,
                    m => m.Value.ValueKind == JsonValueKind.Object ? Value(m.Value, "gid") : Scalar(m.Value),
                    StringComparer.OrdinalIgnoreCase)))
            .ToList();
        // SteamCMD normally uses numeric object keys; accept arrays for compatible providers too.
        var launch = Child(config, "launch");
        var launches = launch.ValueKind == JsonValueKind.Array
            ? launch.EnumerateArray().Select((v, i) => ParseLaunch(i.ToString(CultureInfo.InvariantCulture), v)).ToList()
            : Properties(launch).Select(p => ParseLaunch(p.Name, p.Value)).ToList();
        return new SteamAppInfo(appId, Value(common, "name"),
            Properties(Child(common, "name_localized")).ToDictionary(p => p.Name, p => Scalar(p.Value), StringComparer.OrdinalIgnoreCase),
            Value(config, "installdir"), builds, depotList, launches);
    }

    private static SteamLaunchInfo ParseLaunch(string id, JsonElement item) =>
        new(id, Value(item, "description"), Value(item, "executable"), Value(item, "arguments"), Value(item, "type"), Filter(item));

    private static SteamPlatformFilter Filter(JsonElement item)
    {
        var config = Child(item, "config");
        return new(Value(config, "oslist"), Value(config, "osarch"), Value(config, "language"));
    }

    private static JsonElement Child(JsonElement item, string key) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(key, out var value) ? value : default;
    private static string Value(JsonElement item, string key) => Scalar(Child(item, key));
    private static string Scalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? "",
        JsonValueKind.Number => value.GetRawText(),
        _ => ""
    };
    private static IEnumerable<JsonProperty> Properties(JsonElement item) =>
        item.ValueKind == JsonValueKind.Object ? item.EnumerateObject() : Enumerable.Empty<JsonProperty>();
}
