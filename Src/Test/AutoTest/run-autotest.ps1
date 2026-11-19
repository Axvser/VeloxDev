<#
.SYNOPSIS
    Builds the transition demos and runs the real-window acceptance suite with one command.

.DESCRIPTION
    The one-click form of AutoTest/AGENTS.md §1: build each selected demo with the exact command that
    avoids the stale-binary trap (§4.1), set VELOXDEV_AT=1 plus the fast pace, then run
    VeloxDev.AT.csproj by path. The suite is deliberately absent from VeloxDev.slnx, so the
    repository-root `dotnet test` never opens a window.

    The script only builds demos; it does not build the AT project itself (dotnet test does that).

.PARAMETER Platform
    One or more of WPF, WinForms, Avalonia, MAUI, WinUI, Jalium, Blazor. Defaults to all seven.
    The same subset is passed to the suite as VELOXDEV_AT_PLATFORMS.

.PARAMETER Watch
    Keeps the human-watchable pace (VELOXDEV_AT_PACE=2500, VELOXDEV_AT_OBSERVE=1200,
    VELOXDEV_BENCH_MS=1600) instead of the fast verdict pace.

.PARAMETER NoBuild
    Skips the demo builds. Use only when the demos are already built from the current sources.

.PARAMETER Filter
    An MSTest filter passed through unchanged, e.g. "TestCategory=AT.WPF".

.EXAMPLE
    ./Src/Test/AutoTest/run-autotest.ps1

.EXAMPLE
    ./Src/Test/AutoTest/run-autotest.ps1 -Platform WPF -Watch

.EXAMPLE
    ./Src/Test/AutoTest/run-autotest.ps1 -NoBuild -Filter "TestCategory=AT.WPF"
#>
[CmdletBinding()]
param(
    [ValidateSet('WPF', 'WinForms', 'Avalonia', 'MAUI', 'WinUI', 'Jalium', 'Blazor')]
    [string[]] $Platform = @('WPF', 'WinForms', 'Avalonia', 'MAUI', 'WinUI', 'Jalium', 'Blazor'),
    [switch] $Watch,
    [switch] $NoBuild,
    [string] $Filter = ''
)

$ErrorActionPreference = 'Stop'

# 仓库根按 VeloxDev.slnx 找，不数目录层级 —— 这棵树挪位置时脚本不用跟着改。
$repoRoot = $PSScriptRoot
while ($repoRoot -and -not (Test-Path (Join-Path $repoRoot 'VeloxDev.slnx')))
{
    $repoRoot = Split-Path -Parent $repoRoot
}
if (-not $repoRoot)
{
    throw "VeloxDev.slnx was not found above $PSScriptRoot."
}

# 按 AGENTS.md §1 的顺序注册；Blazor 的 demo 在 Demo/Demo 下，和其余六家不同。
$demos = @(
    [pscustomobject]@{ Name = 'WPF';      Project = 'Examples/Transition/WPF/Demo/Demo.csproj' }
    [pscustomobject]@{ Name = 'WinForms'; Project = 'Examples/Transition/WinForms/Demo/Demo.csproj' }
    [pscustomobject]@{ Name = 'Avalonia'; Project = 'Examples/Transition/Avalonia/Demo/Demo.csproj' }
    [pscustomobject]@{ Name = 'MAUI';     Project = 'Examples/Transition/MAUI/Demo/Demo.csproj' }
    [pscustomobject]@{ Name = 'WinUI';    Project = 'Examples/Transition/WinUI/Demo/Demo.csproj' }
    [pscustomobject]@{ Name = 'Jalium';   Project = 'Examples/Transition/Jalium/Demo/Demo.csproj' }
    [pscustomobject]@{ Name = 'Blazor';   Project = 'Examples/Transition/Blazor/Demo/Demo/Demo.csproj' }
)

$selected = foreach ($name in $Platform)
{
    $demo = $demos | Where-Object { $_.Name -eq $name } | Select-Object -First 1
    if (-not $demo) { throw "No demo project is registered for '$name'." }
    $demo
}

if (-not $NoBuild)
{
    foreach ($demo in $selected)
    {
        Write-Host "==> building $($demo.Name) demo" -ForegroundColor Cyan
        & dotnet build (Join-Path $repoRoot $demo.Project) -c Debug --nologo -v minimal
        if ($LASTEXITCODE -ne 0)
        {
            throw "the $($demo.Name) demo failed to build; refusing to run the suite against a stale binary (AGENTS.md §4.1)"
        }
    }
}

$env:VELOXDEV_AT = '1'
$env:VELOXDEV_AT_PLATFORMS = ($selected.Name -join ',')

if ($Watch)
{
    $env:VELOXDEV_AT_PACE = '2500'
    $env:VELOXDEV_AT_OBSERVE = '1200'
    $env:VELOXDEV_BENCH_MS = '1600'
}
else
{
    $env:VELOXDEV_AT_PACE = '0'
    $env:VELOXDEV_AT_OBSERVE = '0'
    $env:VELOXDEV_BENCH_MS = '200'
}

$mode = if ($Watch) { 'watchable' } else { 'fast' }
Write-Host "==> acceptance: $($selected.Name -join ', ') ($mode)" -ForegroundColor Cyan

$testArgs = @('test', (Join-Path $PSScriptRoot 'VeloxDev.AT.csproj'), '-c', 'Debug', '--nologo')
if ($Filter) { $testArgs += @('--filter', $Filter) }

& dotnet @testArgs
exit $LASTEXITCODE
