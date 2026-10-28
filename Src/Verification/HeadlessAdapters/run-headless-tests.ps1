#!/usr/bin/env pwsh
# Runs every headless adapter suite by path. Deliberately not part of VeloxDev.slnx:
# the solution is what root `dotnet test` resolves, and these projects need UI frameworks.
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path

$projects = @(
    Join-Path $root 'VeloxDev.WPF.Headless.Tests/VeloxDev.WPF.Headless.Tests.csproj'
    Join-Path $root 'VeloxDev.Avalonia.Headless.Tests/VeloxDev.Avalonia.Headless.Tests.csproj'
    Join-Path $root 'VeloxDev.WinForms.Headless.Tests/VeloxDev.WinForms.Headless.Tests.csproj'
    Join-Path $root 'VeloxDev.Razor.Headless.Tests/VeloxDev.Razor.Headless.Tests.csproj'
    Join-Path $root 'VeloxDev.Jalium.Headless.Tests/VeloxDev.Jalium.Headless.Tests.csproj'
)

$failed = @()

foreach ($project in $projects)
{
    Write-Host "==> $project"
    dotnet test $project --nologo -v minimal
    if ($LASTEXITCODE -ne 0)
    {
        $failed += $project
    }
}

if ($failed.Count -gt 0)
{
    Write-Error "headless adapter tests failed: $($failed -join ', ')"
    exit 1
}

Write-Host 'all headless adapter tests passed'
