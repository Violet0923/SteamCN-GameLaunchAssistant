using System.Net;
using WetheringWavesSteamHelper_WinUI.Models;
using WetheringWavesSteamHelper_WinUI.Services;

// Dependency-free regression runner: dotnet run --project Tests/SteamAppInfo.Tests
var checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAILED: " + name);
    Console.WriteLine("PASS: " + name);
    checks++;
}

const string json = """
{
  "status":"success", "data":{"123":{
    "common":{"name":"Example Game","name_localized":{"schinese":"示例游戏"}},
    "config":{"installdir":"Example Game","launch":{
      "0":{"executable":"Bin/Launcher.exe","description":"DirectX 11","arguments":"-dx11","config":{"oslist":"windows"}},
      "1":{"executable":"bin\\launcher.EXE","description":"DirectX 12","arguments":"-dx12","config":{"oslist":"windows"}},
      "2":{"executable":"linux/run.sh","config":{"oslist":"linux"}}
    }},
    "depots":{
      "branches":{"public":{"buildid":12345},"beta":{"buildid":"777"}},
      "124":{"name":"Windows content","config":{"oslist":"windows","osarch":"64"},
        "manifests":{"public":{"gid":18446744073709551615},"beta":"111"}},
      "125":{"config":{"oslist":"linux"},"manifests":{"public":"222"}},
      "126":{"dlcappid":"999","manifests":{"public":"333"}},
      "127":{"config":{"language":"japanese"},"manifests":{"public":"444"}}
    }
  }}
}
""";

var app = SteamAppInfoParser.Parse(json, "123");
var options = new SteamAppSelectionOptions();
var selection = SteamAppSelectionService.Select(app, options);
Check(selection.DisplayName == "示例游戏" && selection.InstallDirectory == "Example Game", "localized name does not replace install directory");
Check(selection.Depots.Count == 1 && selection.Depots[0].Depot.Id == "124", "platform/language/DLC filtering excludes inappropriate depots");
Check(selection.Depots[0].Manifest == "18446744073709551615" && selection.BuildId == "12345", "large numeric IDs preserve precision");
Check(selection.Executables.Count == 1 && selection.Executables[0].Path == @"Bin\Launcher.exe"
    && selection.Executables[0].LaunchOptions.Count == 2, "same EXE with different arguments is deduplicated and subdirectory retained");
Check(app.Depots.Count == 4 && app.BranchBuildIds.Count == 2 && app.LaunchOptions.Count == 3, "raw model retains metadata for future policies");
var beta = SteamAppSelectionService.Select(app, options with { Branch = "beta", Language = "english" });
Check(beta.BuildId == "777" && beta.Depots[0].Manifest == "111" && beta.DisplayName == "Example Game", "branch and language are configurable");

var commonFilter = new SteamPlatformFilter("", "", "");
var multi = app with
{
    Depots = app.Depots.Concat(new[] { new SteamDepotInfo("128", "Shared resources", commonFilter, "",
        new Dictionary<string,string> { ["public"] = "555" }) }).ToList(),
    LaunchOptions = app.LaunchOptions.Concat(new[] { new SteamLaunchInfo("3", "Setup", "Setup.exe", "", "", commonFilter) }).ToList()
};
var multiple = SteamAppSelectionService.Select(multi, options);
Check(multiple.Depots.Count == 2 && multiple.Warnings.Any(w => w.Contains("共同使用")), "multiple depots remain candidates with single-depot limitation");
Check(multiple.Executables.Count == 2, "different EXEs remain separate candidates");
Check(multiple.Depots.Single(d => d.Depot.Id == "128").Manifest == "555", "each candidate keeps its own manifest");
var bit32 = new SteamLaunchInfo("4", "32-bit", "Client32.exe", "", "", new("windows", "32", ""));
Check(SteamAppSelectionService.Select(app with { LaunchOptions = new[] { bit32 } }, options).Executables.Count == 1,
    "32-bit launchers remain usable on 64-bit Windows");
Check(SteamAppSelectionService.Select(app, options with { Architecture = "32" }).Depots.Count == 0,
    "64-bit-only depots excluded on 32-bit Windows");

var invalid = app with
{
    InstallDirectory = @"..\outside",
    LaunchOptions = new[] { new SteamLaunchInfo("0", "", @"..\run.exe", "", "", commonFilter),
        new SteamLaunchInfo("1", "", "start.bat", "", "", commonFilter) },
    BranchBuildIds = new Dictionary<string,string>(),
    Depots = new[] { app.Depots[0] with { Manifests = new Dictionary<string,string>() } }
};
var missing = SteamAppSelectionService.Select(invalid, options);
Check(missing.InstallDirectory == "" && missing.Executables.Count == 0 && missing.Warnings.Count > 0,
    "unsafe or unsupported paths cannot autofill");
Check(missing.BuildId == "" && missing.Depots[0].Manifest == "", "missing branch metadata stays missing");
foreach (var path in new[] { @"C:\bad.exe", @"\bad.exe", @"..\bad.exe", "A/../bad.exe", "CON.exe", "A./bad.exe", "A /bad.exe", "A//bad.exe", "A\nbad.exe", "start.bat" })
    Check(!SteamPathValidator.TryValidate(path, out _, requireExe:true), "reject path " + path.Replace("\n", "\\n"));
Check(SteamPathValidator.TryValidate(@"子目录\Game Launcher.exe", out _, true), "relative EXE paths support spaces and Unicode");
Check(SteamAppInfoService.TryNormalizeAppId(" 000123 ", out var normalized) && normalized == "123", "AppID normalization");
foreach (var id in new[] { "0", "-1", "+1", "1.2", "4294967296", "abc" })
    Check(!SteamAppInfoService.TryNormalizeAppId(id, out _), "reject AppID " + id);

var sparse = SteamAppInfoParser.Parse("""{"status":"success","data":{"1":{"common":{"name":"Sparse"},"config":{"launch":[{"executable":"A.exe"}]}}}}""", "1");
Check(sparse.LaunchOptions.Count == 1 && sparse.Depots.Count == 0 && sparse.InstallDirectory == "", "array launch options and missing optional sections supported");
try
{
    SteamAppInfoParser.Parse("""{"status":"success","data":{"123":{}}}""", "123");
    throw new Exception("FAILED: empty metadata accepted");
}
catch (SteamAppInfoException) { Check(true, "empty metadata produces clear failure"); }

var calls = 0;
using var http = new HttpClient(new StubHandler((request, token) =>
{
    calls++;
    Check(request.RequestUri == new Uri("https://example.test/info/123"), "injectable endpoint and normalized request");
    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
}));
var service = new SteamAppInfoService(http, new Uri("https://example.test/info/"));
var fetched = await service.GetAppInfoAsync("000123");
Check(calls == 1 && fetched.Name == "Example Game" && fetched.Depots.Count == 4, "one HTTP request supplies all metadata");

async Task CheckServiceError(HttpStatusCode status, string body, string expected)
{
    using var client = new HttpClient(new StubHandler((r,t) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) })));
    try { await new SteamAppInfoService(client).GetAppInfoAsync("123"); throw new Exception("FAILED: expected service error"); }
    catch (SteamAppInfoException ex) { Check(ex.Message.Contains(expected), "service failure " + expected); }
}
await CheckServiceError(HttpStatusCode.NotFound, "", "未找到");
await CheckServiceError(HttpStatusCode.ServiceUnavailable, "", "无法连接");
await CheckServiceError(HttpStatusCode.OK, "not json", "格式异常");
await CheckServiceError(HttpStatusCode.OK, """{"status":"error","data":"unknown"}""", "未返回");

using var cancellation = new CancellationTokenSource();
using var waitingClient = new HttpClient(new StubHandler(async (r,t) =>
{
    await Task.Delay(Timeout.Infinite, t);
    return new HttpResponseMessage(HttpStatusCode.OK);
}));
var pending = new SteamAppInfoService(waitingClient).GetAppInfoAsync("123", cancellation.Token);
cancellation.Cancel();
try { await pending; throw new Exception("FAILED: cancellation ignored"); }
catch (OperationCanceledException) { Check(true, "caller cancellation propagates for obsolete requests"); }
using var timeoutClient = new HttpClient(new StubHandler((r,t) => throw new TaskCanceledException()));
try { await new SteamAppInfoService(timeoutClient).GetAppInfoAsync("123"); throw new Exception("FAILED: timeout ignored"); }
catch (SteamAppInfoException ex) { Check(ex.Message.Contains("超时"), "timeout is distinguished from caller cancellation"); }

if (args.Contains("--live"))
{
    var live = await new SteamAppInfoService().GetAppInfoAsync("4162040");
    var result = SteamAppSelectionService.Select(live, options);
    Check(result.Depots.Count > 0 && result.Executables.Count > 0 && result.BuildId != "", "live API provides usable metadata");
    Console.WriteLine($"Live: {result.DisplayName} | {result.InstallDirectory} | {result.Depots[0].Depot.Id} | {result.Executables[0].Path}");
}
Console.WriteLine($"All {checks} checks passed.");

sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request, cancellationToken);
}
