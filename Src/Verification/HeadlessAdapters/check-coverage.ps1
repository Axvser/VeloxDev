#!/usr/bin/env pwsh
# Per-adapter line coverage over the five headless suites, merged with the existing
# headless sampler suite, and gated at a ratchet floor.
#
# "100%" here is contract coverage: SurfaceContractTests in each suite pins the seven roles
# and every public attached point. Line coverage is reported per adapter; the sampler
# coverage comes from Examples/Transition/AUTO TEST/Samplers, which already exercises every
# registered sampler without a desktop.
param(
    [double]$WpfFloor = 44.0,
    [double]$AvaloniaFloor = 55.0,
    [double]$WinFormsFloor = 35.0,
    [double]$RazorFloor = 41.0,
    [double]$JaliumFloor = 35.0,
    [switch]$NoGate
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$out = Join-Path $env:TEMP 'veloxdev-headless-coverage'
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue

$projects = @(
    @{ Name = 'WPF';      Project = 'VeloxDev.WPF.Headless.Tests/VeloxDev.WPF.Headless.Tests.csproj';           Assembly = 'VeloxDev.WPF';      Floor = $WpfFloor }
    @{ Name = 'Avalonia'; Project = 'VeloxDev.Avalonia.Headless.Tests/VeloxDev.Avalonia.Headless.Tests.csproj'; Assembly = 'VeloxDev.Avalonia'; Floor = $AvaloniaFloor }
    @{ Name = 'WinForms'; Project = 'VeloxDev.WinForms.Headless.Tests/VeloxDev.WinForms.Headless.Tests.csproj'; Assembly = 'VeloxDev.WinForms'; Floor = $WinFormsFloor }
    @{ Name = 'Razor';    Project = 'VeloxDev.Razor.Headless.Tests/VeloxDev.Razor.Headless.Tests.csproj';       Assembly = 'VeloxDev.Razor';    Floor = $RazorFloor }
    @{ Name = 'Jalium';   Project = 'VeloxDev.Jalium.Headless.Tests/VeloxDev.Jalium.Headless.Tests.csproj';     Assembly = 'VeloxDev.Jalium';   Floor = $JaliumFloor }
)

function Get-LineHits([string]$File, [string]$Assembly)
{
    [xml]$document = Get-Content $File
    $package = $document.coverage.packages.package |
        Where-Object { $_.name -eq $Assembly } |
        Select-Object -First 1
    if (-not $package) { return @{} }

    $hits = @{}
    foreach ($class in $package.classes.class)
    {
        foreach ($line in $class.lines.line)
        {
            $key = '{0}|{1}' -f $class.name, $line.number
            $hits[$key] = ([int]$line.hits -gt 0)
        }
    }
    return $hits
}

$rows = @()
$failed = @()
$adapterHits = @{}

foreach ($entry in $projects)
{
    $project = Join-Path $root $entry.Project
    $results = Join-Path $out $entry.Name

    Write-Host "==> $($entry.Name)"
    dotnet test $project --nologo -v quiet --collect:"XPlat Code Coverage" --results-directory $results
    if ($LASTEXITCODE -ne 0)
    {
        $failed += "$($entry.Name): test run failed"
        continue
    }

    $coverageFile = Get-ChildItem -Path $results -Recurse -Filter 'coverage.cobertura.xml' |
        Select-Object -First 1
    if (-not $coverageFile)
    {
        $failed += "$($entry.Name): no coverage file"
        continue
    }

    $adapterHits[$entry.Name] = Get-LineHits $coverageFile.FullName $entry.Assembly
}

# Sampler coverage: one headless run covers every adapter's sampler classes. Merge it in.
$samplerProject = Join-Path $root '../../../Examples/Transition/AUTO TEST/Samplers/VeloxDev.SamplerTest.csproj'
$samplerResults = Join-Path $out 'Samplers'
Write-Host '==> Samplers (merge)'
dotnet test $samplerProject --nologo -v quiet --collect:"XPlat Code Coverage" --results-directory $samplerResults
if ($LASTEXITCODE -ne 0)
{
    $failed += 'Samplers: test run failed'
}
else
{
    $samplerCoverage = Get-ChildItem -Path $samplerResults -Recurse -Filter 'coverage.cobertura.xml' |
        Select-Object -First 1

    if ($samplerCoverage)
    {
        foreach ($entry in $projects)
        {
            $samplerHits = Get-LineHits $samplerCoverage.FullName $entry.Assembly
            if ($adapterHits.ContainsKey($entry.Name))
            {
                $merged = $adapterHits[$entry.Name]
                foreach ($key in $samplerHits.Keys)
                {
                    $merged[$key] = ($merged[$key] -eq $true) -or ($samplerHits[$key] -eq $true)
                }
                $adapterHits[$entry.Name] = $merged
            }
            else
            {
                $adapterHits[$entry.Name] = $samplerHits
            }
        }
    }
    else
    {
        $failed += 'Samplers: no coverage file'
    }
}

foreach ($entry in $projects)
{
    if (-not $adapterHits.ContainsKey($entry.Name)) { continue }

    $hits = $adapterHits[$entry.Name]
    $valid = $hits.Count
    $covered = @($hits.Keys | Where-Object { $hits[$_] -eq $true }).Count
    $percent = if ($valid -gt 0) { [math]::Round(100.0 * $covered / $valid, 1) } else { 0.0 }

    $rows += [pscustomobject]@{
        Adapter = $entry.Name
        Covered = $covered
        Lines   = $valid
        Percent = $percent
        Floor   = $entry.Floor
    }

    if (-not $NoGate -and $percent -lt $entry.Floor)
    {
        $failed += "$($entry.Name): $percent% < floor $($entry.Floor)%"
    }
}

$rows | Format-Table -AutoSize | Out-String -Width 120 | Write-Host

if ($failed.Count -gt 0)
{
    Write-Error ("coverage gate failed:`n  " + ($failed -join "`n  "))
    exit 1
}

Write-Host 'coverage gate passed'
