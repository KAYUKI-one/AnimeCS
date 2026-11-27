param(
    [Parameter(Mandatory)][string]$UnityEditor,
    [string]$UnityProject = (Split-Path $PSScriptRoot),
    [string]$Output,
    [string]$BattleMap,
    [ValidateSet('Debian13','Windows')][string]$Platform = 'Debian13'
)
$ErrorActionPreference = 'Stop'
$UnityProject = (Resolve-Path -LiteralPath $UnityProject).Path
$UnityEditor = (Resolve-Path -LiteralPath $UnityEditor).Path
$linuxBuild = $Platform -eq 'Debian13'
$platformFolder = if ($linuxBuild) { 'Builds/Debian13' } else { 'Builds/Windows/Services' }
if (-not $Output) { $Output = Join-Path (Split-Path $PSScriptRoot) $platformFolder }
$Output = [IO.Path]::GetFullPath($Output)
$runtime = if ($linuxBuild) { 'linux-x64' } else { 'win-x64' }
$null = New-Item -ItemType Directory -Force -Path $Output
foreach ($role in 'Data', 'HotUpdate') {
    $project = Join-Path $PSScriptRoot "$role/AnimeCS.$role.csproj"
    dotnet publish $project -c Release -r $runtime --self-contained true -o (Join-Path $Output $role)
    if ($LASTEXITCODE -ne 0) { throw "$role publish failed" }
}
$previousOutput = $env:ANIMECS_BATTLE_OUTPUT
try {
    $battleBinary = if ($linuxBuild) { 'Battle/AnimeCS.Battle.x86_64' } else { 'Battle/AnimeCS.Battle.exe' }
    $buildTarget = if ($linuxBuild) { 'linux64' } else { 'win64' }
    $buildMethod = if ($linuxBuild) { 'BuildLinux' } else { 'BuildWindows' }
    $env:ANIMECS_BATTLE_OUTPUT = Join-Path $Output $battleBinary
    $start = [Diagnostics.ProcessStartInfo]::new($UnityEditor)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $options = @('-batchmode', '-nographics', '-quit', '-projectPath', $UnityProject,
        '-buildTarget', $buildTarget, '-standaloneBuildSubtarget', 'Server',
        '-executeMethod', "AnimeCS.Editor.BattleServerBuild.$buildMethod", '-logFile', (Join-Path $Output 'battle-build.log'))
    if ($BattleMap) { $options += @('-battleMap', $BattleMap) }
    foreach ($option in $options) { $start.ArgumentList.Add($option) }
    $process = [Diagnostics.Process]::Start($start)
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "Battle build failed. Check $Output/battle-build.log" }
} finally {
    $env:ANIMECS_BATTLE_OUTPUT = $previousOutput
}
if (-not $linuxBuild) { return }
if ($IsWindows -and (Get-Command py -ErrorAction SilentlyContinue)) {
    py -3 (Join-Path $PSScriptRoot 'package.py') $Output
} else {
    python3 (Join-Path $PSScriptRoot 'package.py') $Output
}
if ($LASTEXITCODE -ne 0) { throw 'Could not package the Linux services' }
