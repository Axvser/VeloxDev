<#
.SYNOPSIS
    Runs one UI probe with the discipline this repo learned the hard way, then hands the run to
    agent-ui-harness.ps1 (which owns the actual input).

.DESCRIPTION
    Five traps, each of which has cost at least one round of wrong conclusions:

      1. A build error leaves the OLD binary in place, and a stale binary answers exactly like a code bug —
         so the project is built before every run and the run aborts when the build fails;
      2. every trimmed demo builds to Demo.exe, so a leftover instance from another family answers the same
         process-name lookup and the harness screenshots the wrong application (2026-10-11: cost two rounds,
         across two different families) — so leftovers are killed and a foreign Demo is reported loudly;
      3. a screenshot is only worth reading once the window title proves which app it came from — so the title
         is checked against -ExpectTitle and the run FAILS when it does not match;
      4. the HUD's numbers are the usual instrument and they sit in a corner of a 1280x800 shot — so -Crop cuts
         them out and reading is a glance rather than a hunt;
      5. "did it change?" is answered by a diff against a saved baseline, never by eye.

.PARAMETER Project
    The demo project to build. Relative to the repo root, or absolute.

.PARAMETER Exe
    The built executable to run. Relative to the repo root, or absolute.

.PARAMETER Framework
    Passed to the build as -f, for a multi-targeted project (MAUI needs it).

.PARAMETER Actions
    The harness action string, verbatim. See agent-ui-harness.ps1 for the verbs.

.PARAMETER ExpectTitle
    A substring the captured window's title must contain. Strongly recommended: without it, trap 3 is unguarded.

.PARAMETER Crop
    "x,y,w,h", window-relative — crops every shot into <name>-crop.png as well.

.PARAMETER Baseline
    Compare every shot against <OutDir>\baseline-<name>.png and print the differing-pixel count.

.PARAMETER SaveBaseline
    Write those baselines instead of comparing against them.

.EXAMPLE
    ./agent-ui-probe.ps1 `
      -Project "Examples/Workflow/WPF Trimmed/Demo/Demo.csproj" `
      -Exe "Examples/Workflow/WPF Trimmed/Demo/bin/Debug/net10.0-windows/Demo.exe" `
      -ExpectTitle "WPF Trimmed" -Crop "60,560,340,120" `
      -Actions "wheel:640,400,-120; wait:700; shot:plain"
#>
param(
    [Parameter(Mandatory = $true)][string] $Project,
    [Parameter(Mandatory = $true)][string] $Exe,
    [string] $Framework = '',
    [Parameter(Mandatory = $true)][string] $Actions,
    [string] $ExpectTitle = '',
    [string] $Crop = '',
    [string] $Baseline = '',
    [switch] $SaveBaseline,
    [string] $OutDir = "$env:TEMP\veloxprobe",
    [int] $Tolerance = 8
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$harness = Join-Path $PSScriptRoot 'agent-ui-harness.ps1'
if (-not (Test-Path $harness)) { throw "agent-ui-harness.ps1 not found next to this script" }

$exePath = if ([IO.Path]::IsPathRooted($Exe)) { $Exe } else { Join-Path $root $Exe }
$processName = [IO.Path]::GetFileNameWithoutExtension($exePath)

function Stop-Leftovers {
    # Trap 2. The harness looks a window up BY PROCESS NAME.
    $mine = @(Get-Process -Name $processName -ErrorAction SilentlyContinue)
    foreach ($p in $mine) { try { $p.Kill() } catch { } }
    if ($mine.Count -gt 0) { Write-Host "killed $($mine.Count) leftover $processName" -ForegroundColor DarkGray }

    $foreign = @(Get-Process -Name 'Demo', 'Demo.Desktop' -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path -ne $exePath })
    if ($foreign.Count -gt 0) {
        Write-Host "WARNING: another demo is running and will steal the window lookup:" -ForegroundColor Yellow
        foreach ($p in $foreign) { Write-Host "  $($p.Id)  $($p.Path)" -ForegroundColor Yellow }
        Write-Host "  (kill it, or the numbers you are about to read are its)" -ForegroundColor Yellow
    }
}

# Trap 1. A failed build leaves the old binary, and the old binary looks exactly like a code bug.
Write-Host "building $Project ..." -ForegroundColor Cyan
$build = if ($Framework) { & dotnet build $Project -f $Framework -c Debug --nologo -v m 2>&1 }
else { & dotnet build $Project -c Debug --nologo -v m 2>&1 }
if ($LASTEXITCODE -ne 0) {
    $build | Select-Object -Last 25 | ForEach-Object { Write-Host $_ }
    throw "build failed — refusing to probe, because a stale binary answers exactly like a code bug"
}
Write-Host ($build | Select-String '个错误|error' | Select-Object -Last 1) -ForegroundColor DarkGray
if (-not (Test-Path $exePath)) { throw "exe not found after a clean build: $exePath" }
Write-Host "exe   $exePath" -ForegroundColor DarkGray
Write-Host "built $((Get-Item $exePath).LastWriteTime)" -ForegroundColor DarkGray

Stop-Leftovers
& $harness -Exe $exePath -Actions $Actions -OutDir $OutDir
$log = Join-Path $OutDir 'harness.log'

# Trap 3. Prove which application answered before believing a single number.
$titleLine = Get-Content $log | Where-Object { $_ -like 'window title *' } | Select-Object -Last 1
$title = if ($titleLine) { $titleLine.Substring('window title '.Length) } else { '' }
if (-not $titleLine) { Write-Host "WARNING: the harness logged no window title; cannot prove which app answered" -ForegroundColor Yellow }
else { Write-Host "window title: $title" -ForegroundColor Cyan }
if ($ExpectTitle -and $title -notlike "*$ExpectTitle*") {
    throw "captured the wrong window: title '$title' does not contain '$ExpectTitle' — the numbers in this run belong to another application"
}

Add-Type -AssemblyName System.Drawing

# Trap 4. The HUD is the instrument; crop it out so reading it is a glance.
if ($Crop) {
    $b = $Crop -split ','
    $box = @{ X = [int]$b[0]; Y = [int]$b[1]; W = [int]$b[2]; H = [int]$b[3] }
    Get-ChildItem (Join-Path $OutDir '*.png') -Exclude '*-crop.png' | ForEach-Object {
        $src = New-Object System.Drawing.Bitmap($_.FullName)
        $dst = New-Object System.Drawing.Bitmap($box.W, $box.H)
        $g = [System.Drawing.Graphics]::FromImage($dst)
        $g.DrawImage($src, (New-Object System.Drawing.Rectangle(0, 0, $box.W, $box.H)),
            (New-Object System.Drawing.Rectangle($box.X, $box.Y, $box.W, $box.H)),
            [System.Drawing.GraphicsUnit]::Pixel)
        $out = Join-Path $OutDir ($_.BaseName + '-crop.png')
        $dst.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
        $g.Dispose(); $dst.Dispose(); $src.Dispose()
        Write-Output "crop $out"
    }
}

# Trap 5. "Did it change?" is a number, not an impression.
if ($Baseline -or $SaveBaseline) {
    Get-ChildItem (Join-Path $OutDir '*.png') -Exclude '*-crop.png', 'baseline-*.png' | ForEach-Object {
        $base = Join-Path $OutDir ("baseline-$Baseline.png")
        if ($SaveBaseline) {
            Copy-Item $_.FullName $base -Force
            Write-Output "baseline $base"
        }
        elseif (Test-Path $base) {
            $a = New-Object System.Drawing.Bitmap($base)
            $bb = New-Object System.Drawing.Bitmap($_.FullName)
            if ($a.Width -ne $bb.Width -or $a.Height -ne $bb.Height) {
                Write-Output "diff $($_.Name): size changed $($a.Width)x$($a.Height) -> $($bb.Width)x$($bb.Height)"
            }
            else {
                $n = 0
                for ($y = 0; $y -lt $a.Height; $y++) {
                    for ($x = 0; $x -lt $a.Width; $x++) {
                        $pa = $a.GetPixel($x, $y); $pb = $bb.GetPixel($x, $y)
                        if (([Math]::Abs($pa.R - $pb.R) + [Math]::Abs($pa.G - $pb.G) + [Math]::Abs($pa.B - $pb.B)) -gt $Tolerance) { $n++ }
                    }
                }
                Write-Output "diff $($_.Name): $n px differ from baseline-$Baseline"
            }
            $a.Dispose(); $bb.Dispose()
        }
        else { Write-Output "diff $($_.Name): no baseline-$Baseline.png (run with -SaveBaseline first)" }
    }
}
