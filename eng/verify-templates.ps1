<#
.SYNOPSIS
Generates each VeloxDev Workflow item-template set into a scratch project and compiles it.

The template-pack build (`dotnet pack`) does NOT compile the working/content files, so API
misuse and cross-template type-name mismatches only surface when a user generates the templates.
This script closes that gap: for each framework it packs + installs the templates, generates all
seven into a scratch project referencing the local adapter, and builds it.

.PARAMETER Framework
"all" (default) or one of WPF | WinUI | Avalonia | WinForms | MAUI | Razor | Jalium.
MAUI requires the MAUI workload and is skipped unless explicitly requested.

.PARAMETER Output
Scratch directory for generated projects (default .scratch/tplverify under the repo).
#>
param(
    [string]$Framework = "all",
    [string]$Output = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
if (-not $Output) { $Output = Join-Path $root ".scratch\tplverify" }

# Framework -> template-pack path, adapter path, and shortName prefix.
$packs = @(
    @{ fw = "WPF";      pack = "Src/Templates/VeloxDev.WPF.Templates/working/VeloxDev.WPF.Templates.csproj";       adapter = "Src/Adapters/VeloxDev.WPF/VeloxDev.WPF.csproj";      short = "wpf-v" }
    @{ fw = "WinUI";    pack = "Src/Templates/VeloxDev.WinUI.Templates/working/VeloxDev.WinUI.Templates.csproj";   adapter = "Src/Adapters/VeloxDev.WinUI/VeloxDev.WinUI.csproj";  short = "winui-v" }
    @{ fw = "Avalonia"; pack = "Src/Templates/VeloxDev.Avalonia.Templates/working/VeloxDev.Avalonia.Templates.csproj"; adapter = "Src/Adapters/VeloxDev.Avalonia/VeloxDev.Avalonia.csproj"; short = "ava-v" }
    @{ fw = "WinForms"; pack = "Src/Templates/VeloxDev.WinForms.Templates/working/VeloxDev.WinForms.Templates.csproj"; adapter = "Src/Adapters/VeloxDev.WinForms/VeloxDev.WinForms.csproj"; short = "winforms-v" }
    @{ fw = "MAUI";     pack = "Src/Templates/VeloxDev.MAUI.Templates/working/VeloxDev.MAUI.Templates.csproj";     adapter = "Src/Adapters/VeloxDev.MAUI/VeloxDev.MAUI.csproj";      short = "maui-v";  workload = $true }
    @{ fw = "Razor";    pack = "Src/Templates/VeloxDev.Razor.Templates/working/VeloxDev.Razor.Templates.csproj";   adapter = "Src/Adapters/VeloxDev.Razor/VeloxDev.Razor.csproj";  short = "razor-v" }
    @{ fw = "Jalium";   pack = "Src/Templates/VeloxDev.Jalium.Templates/VeloxDev.Jalium.Templates.csproj"; adapter = "Src/Adapters/VeloxDev.Jalium/VeloxDev.Jalium.csproj"; short = "jalium-v" }
)

function Write-HostCsproj([string]$fw, [string]$dir) {
    $body = switch ($fw) {
        "WPF" {
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
"@
        }
        "WinForms" {
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
"@
        }
        "Avalonia" {
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Avalonia" Version="11.*" />
  </ItemGroup>
</Project>
"@
        }
        "WinUI" {
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.WindowsAppSDK" Version="1.6.*" />
  </ItemGroup>
</Project>
"@
        }
        "Razor" {
@"
<Project Sdk="Microsoft.NET.Sdk.Razor">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
</Project>
"@
        }
        "Jalium" {
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Jalium.UI.Desktop" Version="26.10.8" />
  </ItemGroup>
</Project>
"@
        }
        default { throw "Unsupported framework: $fw" }
    }
    Set-Content -Path (Join-Path $dir "Verify.csproj") -Value $body -Encoding utf8
}

function Invoke-Framework([hashtable]$cfg) {
    $fw = $cfg.fw
    Write-Host "===== $fw ====="
    $defs = @{ "tree" = "TreeView"; "node" = "NodeView"; "slot" = "SlotView"; "link" = "LinkView"; "decorator" = "GridDecorator"; "minimap" = "MinimapOverlay"; "selector" = "TemplateSelector" }
    $order = @("tree", "node", "slot", "link", "decorator", "minimap", "selector")

    $out = Join-Path $Output $fw
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    New-Item -ItemType Directory -Path $out -Force | Out-Null
    Write-HostCsproj $fw $out

    # Reference the local adapter from the host project.
    $proj = Join-Path $out "Verify.csproj"
    $content = Get-Content $proj -Raw
    $adapterAbs = (Join-Path $root $cfg.adapter) -replace '\\', '/'
    $itemGroup = "  <ItemGroup>`n    <ProjectReference Include=`"$adapterAbs`" />`n  </ItemGroup>"
    $content = $content -replace "</Project>", "$itemGroup`n</Project>"
    Set-Content -Path $proj -Value $content -Encoding utf8

    # Pack + install the template pack (uninstall first to avoid stale shortName conflicts).
    $packOut = Join-Path $out "pack"
    New-Item -ItemType Directory -Path $packOut -Force | Out-Null
    dotnet pack (Join-Path $root $cfg.pack) -c Debug --nologo -v q -o $packOut | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet pack failed for $fw" }
    $nupkg = Get-ChildItem $packOut -Filter "*.nupkg" | Select-Object -First 1
    if (-not $nupkg) { throw "No nupkg produced for $fw" }
    # Uninstall by pack NAME (the nupkg path may differ from a previously-installed copy), then install.
    $packName = [regex]::Replace($nupkg.BaseName, '\.\d+\.\d+(\.\d+)?$', '')
    dotnet new uninstall $packName 2>$null | Out-Null
    dotnet new install $nupkg.FullName | Out-Host

    # Generate the seven templates with default names into the scratch project.
    foreach ($t in $order) {
        # Jalium's grid-decorator shortName is "-grid", not "-decorator".
        $short = if ($fw -eq "Jalium" -and $t -eq "decorator") { "jalium-v-grid" } else { "$($cfg.short)-$t" }
        $name = $defs[$t]
        $r = dotnet new $short -n $name --namespace MyApp.Views -o $out 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Host "  generate $short -> $name FAILED"
            $r | Select-Object -Last 3 | Out-Host
            throw "generate failed for $fw/$short"
        }
    }
    Write-Host "  generated 7 templates -> $out"

    # Compile.
    Push-Location $out
    try {
        dotnet build -c Debug --nologo -v q 2>&1 | Out-Host
        if ($LASTEXITCODE -ne 0) { Write-Host "  BUILD FAILED ($fw)"; return $false }
    }
    finally { Pop-Location }
    Write-Host "  BUILD OK ($fw)"
    return $true
}

$targets = if ($Framework -eq "all") { $packs } else { $packs | Where-Object { $_.fw -eq $Framework } }
if (-not $targets) { throw "Unknown framework: $Framework" }

$passed = @(); $failed = @()
foreach ($cfg in $targets) {
    if ($cfg.workload) {
        Write-Host "SKIP $($cfg.fw): requires the .NET MAUI workload; run manually."
        continue
    }
    try { if (Invoke-Framework $cfg) { $passed += $cfg.fw } else { $failed += $cfg.fw } }
    catch { Write-Host "  ERROR: $_"; $failed += $cfg.fw }
}
Write-Host ""
Write-Host "===== SUMMARY ====="
Write-Host "PASSED: $($passed -join ', ')"
Write-Host "FAILED: $($failed -join ', ')"
if ($failed.Count -gt 0) { exit 1 }
