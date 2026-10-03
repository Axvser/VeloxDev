<#
.SYNOPSIS
Verifies the Jalium workflow item templates: that they pack and install, that all seven generate and compile
together against the adapter, and that each generated file still matches its mirror under
Examples/Workflow/Jalium Trimmed/.

.DESCRIPTION
Nothing in the repository compiles the template text — the template projects set EnableDefaultCompileItems=false
and IncludeBuildOutput=false, so `dotnet build` over them does nothing and a generated file that does not compile
is invisible to every other check. This script is that check, the Jalium counterpart of
verify-workflow-item-templates.ps1.

It proves: the seven CLI short names and their aliases resolve; every primaryOutputs entry is present; the seven
generated files compile together against the adapter (including the base classes they derive from); and template
text still equals the mirror.

It cannot prove anything about runtime behaviour — drawing, the connection gesture, the zoom pin, the self-bounding
of link views. Run a demo for that.

.PARAMETER Strict
Makes template/mirror drift fatal.

.PARAMETER KeepProbe
Leaves the probe project on disk for inspection instead of deleting it.

.EXAMPLE
powershell -NoProfile -ExecutionPolicy Bypass -File Src/Verification/verify-jalium-item-templates.ps1
#>
[CmdletBinding()]
param(
    [switch] $Strict,
    [switch] $KeepProbe
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$templateProject = Join-Path $repoRoot 'Src\Templates\VeloxDev.Jalium.Templates\VeloxDev.Jalium.Templates.csproj'
$adapterProject = Join-Path $repoRoot 'Src\Adapters\VeloxDev.Jalium\VeloxDev.Jalium.csproj'
$mirrorRoot = Join-Path $repoRoot 'Examples\Workflow\Jalium Trimmed\Demo\Views\Workflow'

# CLI short name -> the class name the mirror uses for it.
$items = [ordered]@{
    'jalium-v-tree'      = 'TreeView'
    'jalium-v-node'      = 'NodeView'
    'jalium-v-slot'      = 'SlotView'
    'jalium-v-link'      = 'LinkView'
    'jalium-v-decorator' = 'GridDecorator'
    'jalium-v-minimap'   = 'MinimapOverlay'
    'jalium-v-selector'  = 'TemplateSelector'
}

$mirrorNamespace = 'Demo.Views.Workflow'
$probeNamespace = 'Probe.Views'

function Invoke-Step([string] $what, [scriptblock] $body) {
    Write-Host "  $what" -ForegroundColor DarkGray
    & $body
    if ($LASTEXITCODE -ne 0) { throw "$what failed (exit $LASTEXITCODE)" }
}

# Uninstalling a pack that was never installed is not an error, but dotnet writes to stderr for it and
# $ErrorActionPreference = 'Stop' turns that into a terminating NativeCommandError.
function Uninstall-TemplatePack {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { dotnet new uninstall VeloxDev.Jalium.Templates 2>&1 | Out-Null } catch { } finally { $ErrorActionPreference = $previous }
}

# The Jalium templates spell their palette as `ColorConverter.ConvertFromString("#RRGGBB")` because a `dotnet new`
# symbol is text; the mirror spells the same colour as `Color.FromRgb(...)` / `Colors.White`. Normalise both sides
# so the comparison sees the shape, not the spelling.
function Normalize-Lines([string] $path, [string] $ns) {
    # -Encoding UTF8 is load-bearing: Windows PowerShell 5.1 otherwise decodes BOM-less UTF-8 as the system ANSI
    # code page, and the mis-decoded Chinese comments swallow the following newline — which reads as two lines
    # having been joined.
    $text = ((Get-Content $path -Encoding UTF8) -join "`n").Replace($ns, '<ns>')
    $text = [regex]::Replace($text, 'ColorConverter\.ConvertFromString\("[^"]*"\)', '<color>')
    $text = [regex]::Replace($text, 'Color\.FromArgb\([^()]*\)', '<color>')
    $text = [regex]::Replace($text, 'Color\.FromRgb\([^()]*\)', '<color>')
    $text = [regex]::Replace($text, 'Colors\.\w+', '<color>')
    # The template's cast is only there because ConvertFromString returns object; the mirror's factory returns a
    # Color already, so drop the cast before comparing.
    $text = $text.Replace('(Color)<color>', '<color>')
    return ($text -split "`n")
}

$probe = Join-Path ([System.IO.Path]::GetTempPath()) ("veloxdev-jalium-probe-" + [guid]::NewGuid().ToString('N'))
$nupkgs = Join-Path $probe 'nupkgs'

try {
    New-Item -ItemType Directory -Force -Path $probe, $nupkgs | Out-Null

    Write-Host 'Packing and installing the template pack' -ForegroundColor Cyan
    Invoke-Step 'pack' { dotnet pack $templateProject -c Debug -o $nupkgs | Out-Null }
    $nupkg = Get-ChildItem $nupkgs -Filter '*.nupkg' | Select-Object -First 1
    if (-not $nupkg) { throw "no .nupkg was produced in $nupkgs" }

    Uninstall-TemplatePack
    Invoke-Step 'install' { dotnet new install $nupkg.FullName | Out-Null }

    Write-Host 'Generating into a throwaway project' -ForegroundColor Cyan
    # Jalium has no `dotnet new` project template, so the probe shell is written by hand — the minimum a Jalium app
    # needs: the desktop entry package plus the adapter.
    $probeCsproj = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Library</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Jalium.UI.Desktop" Version="26.10.8" />
    <ProjectReference Include="$adapterProject" />
  </ItemGroup>
</Project>
"@
    Set-Content -Path (Join-Path $probe 'Probe.csproj') -Value $probeCsproj -Encoding UTF8

    Push-Location $probe
    try {
        foreach ($shortName in $items.Keys) {
            Invoke-Step "generate $shortName" {
                dotnet new $shortName -n $items[$shortName] -ns $probeNamespace -o Views | Out-Null
            }
        }
    }
    finally {
        Pop-Location
    }

    Write-Host 'Building the generated set' -ForegroundColor Cyan
    Invoke-Step 'build' { dotnet build (Join-Path $probe 'Probe.csproj') -c Debug | Out-Null }

    Write-Host 'Comparing each generated file against its mirror' -ForegroundColor Cyan
    $mismatches = 0
    foreach ($shortName in $items.Keys) {
        $class = $items[$shortName]
        $generated = Join-Path $probe "Views\$class.cs"
        if (-not (Test-Path $generated)) { throw "$shortName produced no $class.cs" }

        $mirror = Join-Path $mirrorRoot "$class.cs"
        if (-not (Test-Path $mirror)) { Write-Warning "no mirror at $mirror"; continue }

        $a = Normalize-Lines $generated $probeNamespace
        $b = Normalize-Lines $mirror $mirrorNamespace

        $firstDiff = -1
        for ($i = 0; $i -lt [Math]::Min($a.Count, $b.Count); $i++) {
            if ($a[$i] -ne $b[$i]) { $firstDiff = $i; break }
        }

        if ($firstDiff -lt 0 -and $a.Count -eq $b.Count) {
            Write-Host "  $shortName matches" -ForegroundColor Green
            continue
        }

        $mismatches++
        $where = if ($firstDiff -lt 0) { "line counts differ ($($a.Count) generated vs $($b.Count) mirror)" } else { "first difference at line $($firstDiff + 1)" }
        Write-Host "  $shortName DIFFERS — $where" -ForegroundColor Red

        $from = [Math]::Max(0, $firstDiff - 3)
        for ($i = $from; $i -lt [Math]::Min($b.Count, $firstDiff + 6); $i++) {
            if ($i -ge $a.Count -or $a[$i] -ne $b[$i]) {
                Write-Host "    gen | $($a[$i])" -ForegroundColor Red
                Write-Host "    mir | $($b[$i])" -ForegroundColor Red
            }
        }
    }

    if ($mismatches -gt 0) {
        $message = "$mismatches item(s) drifted from their mirror"
        if ($Strict) { throw $message }
        Write-Warning "$message — pass -Strict to fail on this"
        exit 2
    }

    Write-Host 'OK' -ForegroundColor Green
}
finally {
    Uninstall-TemplatePack
    if ($KeepProbe) { Write-Host "probe kept at $probe" } else { Remove-Item -Recurse -Force $probe -ErrorAction SilentlyContinue }
}
