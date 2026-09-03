[CmdletBinding()]
param([string]$InnoCompiler)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'SteamCN-GameLaunchAssistant.csproj'
$release = Get-Content -LiteralPath (Join-Path $repoRoot 'version.json') -Raw | ConvertFrom-Json
$version = $release.version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Release version must be major.minor.patch.' }
[xml]$project = Get-Content -LiteralPath $projectPath -Raw
$assemblyName = @($project.Project.PropertyGroup.AssemblyName | Where-Object { $_ })[0]
$projectVersion = @($project.Project.PropertyGroup.Version | Where-Object { $_ })[0]
$appInfo = Get-Content -LiteralPath (Join-Path $repoRoot 'AppInfo.cs') -Raw
[xml]$manifest = Get-Content -LiteralPath (Join-Path $repoRoot 'Package.appxmanifest') -Raw
if ($projectVersion -ne $version -or $appInfo -notmatch ('Version = "v' + [regex]::Escape($version) + '"') -or $manifest.Package.Identity.Version -ne "$version.0") {
    throw 'Synchronize version.json, project Version, AppInfo.Version and package Version before publishing.'
}
if (-not $InnoCompiler) {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
    )
    $InnoCompiler = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $InnoCompiler -or -not (Test-Path -LiteralPath $InnoCompiler)) { throw 'Install Inno Setup 6.5+ or specify -InnoCompiler.' }

# Each build uses a new directory; stale files can never enter a later installer.
$runRoot = Join-Path $repoRoot ('Output\v' + $version + '-' + [Guid]::NewGuid().ToString('N'))
$publishDir = Join-Path $runRoot 'publish'
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
& dotnet publish $projectPath --configuration Release -p:Platform=x64 -r win-x64 --self-contained true `
    -p:WindowsAppSDKSelfContained=true -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false `
    -p:SatelliteResourceLanguages='zh-CN%3Ben-US' --output $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed: $LASTEXITCODE" }
foreach ($required in @("$assemblyName.exe", "$assemblyName.dll", "$assemblyName.deps.json", "$assemblyName.runtimeconfig.json", "$assemblyName.pri", 'coreclr.dll', 'Microsoft.UI.Xaml.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $publishDir $required))) { throw "Publish output missing: $required" }
}
Copy-Item -LiteralPath (Join-Path $repoRoot 'LICENSE') -Destination $publishDir
Copy-Item -LiteralPath (Join-Path $repoRoot 'packaging\languages\LICENSE.txt') -Destination (Join-Path $publishDir 'Inno-Chinese-Translation-LICENSE.txt')
& $InnoCompiler "/DMyAppVersion=$version" "/DSourceDir=$publishDir" "/O$runRoot" (Join-Path $repoRoot 'SteamCN-GameLaunchAssistant.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed: $LASTEXITCODE" }
$installer = Join-Path $runRoot "$assemblyName-v$version-win-x64-setup.exe"
if (-not (Test-Path -LiteralPath $installer)) { throw 'Installer was not produced.' }
$checksum = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $runRoot 'SHA256SUMS.txt'), "$checksum  $([IO.Path]::GetFileName($installer))`n", [Text.UTF8Encoding]::new($false))
Write-Output "Installer: $installer"
Write-Output "SHA256: $checksum"
