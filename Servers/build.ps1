param(
    [Parameter(Mandatory)][string]$UnityEditor,
    [string]$UnityProject = (Split-Path $PSScriptRoot),
    [string]$Output = (Join-Path (Split-Path $PSScriptRoot) 'Builds/Debian13'),
    [string]$BattleMap
)
$ErrorActionPreference = 'Stop'
$UnityProject = (Resolve-Path -LiteralPath $UnityProject).Path
$UnityEditor = (Resolve-Path -LiteralPath $UnityEditor).Path
$Output = [IO.Path]::GetFullPath($Output)
$null = New-Item -ItemType Directory -Force -Path $Output
foreach ($role in 'Data', 'HotUpdate') {
    $project = Join-Path $PSScriptRoot "$role/AnimeCS.$role.csproj"
    dotnet publish $project -c Release -r linux-x64 --self-contained true -o (Join-Path $Output $role)
    if ($LASTEXITCODE -ne 0) { throw "$role publish failed" }
}
$previousOutput = $env:ANIMECS_BATTLE_OUTPUT
try {
    $env:ANIMECS_BATTLE_OUTPUT = Join-Path $Output 'Battle/AnimeCS.Battle.x86_64'
    $start = [Diagnostics.ProcessStartInfo]::new($UnityEditor)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $options = @('-batchmode', '-nographics', '-quit', '-projectPath', $UnityProject,
        '-buildTarget', 'linux64', '-standaloneBuildSubtarget', 'Server',
        '-executeMethod', 'AnimeCS.Editor.BattleServerBuild.BuildLinux', '-logFile', (Join-Path $Output 'battle-build.log'))
    if ($BattleMap) { $options += @('-battleMap', $BattleMap) }
    foreach ($option in $options) { $start.ArgumentList.Add($option) }
    $process = [Diagnostics.Process]::Start($start)
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "Battle build failed. Check $Output/battle-build.log" }
} finally {
    $env:ANIMECS_BATTLE_OUTPUT = $previousOutput
}
if ($IsWindows -and (Get-Command py -ErrorAction SilentlyContinue)) {
    py -3 (Join-Path $PSScriptRoot 'package.py') $Output
} else {
    python3 (Join-Path $PSScriptRoot 'package.py') $Output
}
if ($LASTEXITCODE -ne 0) { throw 'Could not package the Linux services' }
