#requires -Version 7
param(
    [Parameter(Mandatory)][ipaddress] $LanAddress,
    [string] $Services = (Join-Path (Split-Path $PSScriptRoot) 'Builds/Windows/Services'),
    [string] $StateDir = (Join-Path $env:LOCALAPPDATA 'AnimeCS/LanServices')
)
$ErrorActionPreference = 'Stop'
if ($LanAddress.AddressFamily -ne [Net.Sockets.AddressFamily]::InterNetwork -or $LanAddress.Equals([Net.IPAddress]::Any)) {
    throw 'Choose the IPv4 address of the host network adapter.'
}
$octets = $LanAddress.GetAddressBytes()
if (-not ([Net.IPAddress]::IsLoopback($LanAddress) -or $octets[0] -eq 10 -or
        ($octets[0] -eq 172 -and $octets[1] -ge 16 -and $octets[1] -le 31) -or
        ($octets[0] -eq 192 -and $octets[1] -eq 168))) {
    throw 'LAN HTTP requires a private IPv4 address. Use HTTPS endpoints for an internet deployment.'
}
$Services = (Resolve-Path -LiteralPath $Services).Path
$StateDir = [IO.Directory]::CreateDirectory($StateDir).FullName
$children = [Collections.Generic.List[Diagnostics.Process]]::new()
$names = @('Data', 'HotUpdate', 'Battle')
foreach ($role in $names) {
    $exe = Join-Path $Services "$role/AnimeCS.$role.exe"
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Missing $exe" }
}
$keyPath = Join-Path $StateDir 'battle.key'
if (-not (Test-Path -LiteralPath $keyPath)) {
    $key = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    $stream = [IO.File]::Open($keyPath, [IO.FileMode]::CreateNew)
    try { $bytes=[Text.Encoding]::ASCII.GetBytes($key); $stream.Write($bytes,0,$bytes.Length) }
    finally { $stream.Dispose() }
}
$settings = @{
    ANIMECS_DATA_URL="http://${LanAddress}:27841"
    ANIMECS_UPDATE_URL="http://${LanAddress}:27842"
    ANIMECS_BATTLE_DATA_ENDPOINT="http://${LanAddress}:27841"
    ANIMECS_ALLOW_PRIVATE_HTTP='1'
    ANIMECS_BATTLE_PUBLIC_HOST=$LanAddress.ToString()
    ANIMECS_BATTLE_ADDRESS=$LanAddress.ToString()
    ANIMECS_BATTLE_PORT='27840'
    ANIMECS_DATA_DIR=(Join-Path $StateDir 'Data')
    ANIMECS_UPDATE_DIR=(Join-Path $StateDir 'HotUpdate')
    ANIMECS_BATTLE_STATE_DIR=(Join-Path $StateDir 'BattleResults')
    ANIMECS_BATTLE_SECRET=[IO.File]::ReadAllText($keyPath).Trim()
}
try {
    foreach ($role in $names) {
        $exe = Join-Path $Services "$role/AnimeCS.$role.exe"
        $start = [Diagnostics.ProcessStartInfo]::new($exe)
        $start.WorkingDirectory = Split-Path $exe
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
        foreach ($pair in $settings.GetEnumerator()) { $start.Environment[$pair.Key]=$pair.Value }
        if ($role -eq 'Battle') {
            foreach ($option in @('-batchmode','-nographics','-logFile',(Join-Path $StateDir 'battle.log'))) {
                $start.ArgumentList.Add($option)
            }
        }
        $children.Add([Diagnostics.Process]::Start($start))
    }
    Write-Host "客户端地址配置"
    $clientSettings = [ordered]@{dataUrl=$settings.ANIMECS_DATA_URL;updateUrl=$settings.ANIMECS_UPDATE_URL;allowPrivateHttp=$true}
    $publishedManifest = Join-Path $settings.ANIMECS_UPDATE_DIR 'current.json'
    if (Test-Path -LiteralPath $publishedManifest -PathType Leaf) {
        $clientSettings.updateManifestSha256 = (Get-FileHash -LiteralPath $publishedManifest -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    $clientSettings | ConvertTo-Json
    Write-Host "按 Ctrl+C 停止这组服务。"
    while ($true) {
        Start-Sleep -Seconds 1
        foreach ($child in $children) {
            if ($child.HasExited) { throw "Service exited with code $($child.ExitCode)" }
        }
    }
}
finally {
    foreach ($child in $children) {
        if (-not $child.HasExited) { $child.Kill(); $child.WaitForExit() }
        $child.Dispose()
    }
}
