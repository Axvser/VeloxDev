<#
.SYNOPSIS
Verifies the workflow item templates of every platform: that they pack and install, that all seven items generate
and compile together against their adapter, and that each generated file still matches its mirror under
Examples/Workflow/<Platform> Trimmed/.

.DESCRIPTION
Nothing in the repository compiles the template text — the template projects set EnableDefaultCompileItems=false
and IncludeBuildOutput=false, so `dotnet build` over them does nothing and a generated file that does not compile
is invisible to every other check. This script is that check, generalised to all seven platforms.

For a platform it proves: the seven CLI short names resolve; no `replaces` token survives into a generated file (an
un-substituted placeholder means the template references a symbol it does not declare); the seven generated files
compile together against the adapter; and template code/markup still equals the checked-in mirror, after the small
set of mechanical edits the mirror is allowed to carry:

  - the namespace rooted in the demo instead of the probe's,
  - sibling items renamed by the demo (GridDecorator -> WorkflowGridDecorator, TemplateSelector -> CustomTemplateSelector),
  - a colour spelled as a factory call where the template spells it as a string,
  - comments, blank lines, `#region` directives, a 2 vs 2.0 literal, and any pure re-wrapping of long lines,
  - the demo-only canvas-info HUD (the tree item, on every platform),
  - the generation-time "generate the siblings" header comment.

The Avalonia node/link items additionally allow the mirror's `x:DataType` in place of the template's
`x:CompileBindings=False` — the template comment tells the consumer to do exactly that.

It cannot prove runtime behaviour (drawing, panning, the gesture, the menu) and it cannot tell a wrong colour from a
right one — a wrong colour still compiles. Run a demo for that.

.PARAMETER Platform
One or more of WinForms, Jalium, WPF, Avalonia, WinUI, MAUI, Razor. Defaults to all seven.

.PARAMETER Strict
Makes template/mirror drift fatal. Off by default so a first run reports pre-existing drift instead of stopping.

.PARAMETER KeepProbe
Leaves each probe project on disk for inspection instead of deleting it.

.EXAMPLE
powershell -NoProfile -ExecutionPolicy Bypass -File Src/Verification/verify-workflow-item-templates-all.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File Src/Verification/verify-workflow-item-templates-all.ps1 -Platform WPF
#>
[CmdletBinding()]
param(
    [ValidateSet('WinForms', 'Jalium', 'WPF', 'Avalonia', 'WinUI', 'MAUI', 'Razor')]
    [string[]] $Platform = @('WinForms', 'Jalium', 'WPF', 'Avalonia', 'WinUI', 'MAUI', 'Razor'),
    [switch] $Strict,
    [switch] $KeepProbe
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

# The seven item templates are the same seven everywhere; only the CLI prefix, the markup extension and the demo's
# own class names differ. `Kind` is 'markup' when the item ships a markup file plus a code-behind, 'code' otherwise.
$itemDefs = [ordered]@{
    'decorator' = @{ Generated = 'GridDecorator';     Kind = 'code' }
    'link'      = @{ Generated = 'LinkView';          Kind = 'markup' }
    'minimap'   = @{ Generated = 'MinimapOverlay';    Kind = 'code' }
    'node'      = @{ Generated = 'NodeView';          Kind = 'markup' }
    'slot'      = @{ Generated = 'SlotView';          Kind = 'markup' }
    'selector'  = @{ Generated = 'TemplateSelector';  Kind = 'code' }
    'tree'      = @{ Generated = 'TreeView';          Kind = 'markup' }
}
$itemOrder = @('decorator', 'link', 'minimap', 'node', 'slot', 'selector', 'tree')

# The demo renames two items; the mirror carries the demo's name, the template generates the default one.
$renameWpfFamily = [ordered]@{ 'WorkflowGridDecorator' = 'GridDecorator'; 'CustomTemplateSelector' = 'TemplateSelector' }
$renameRazorOnly = [ordered]@{ 'CustomTemplateSelector' = 'TemplateSelector' }
$renameWpfBase = [ordered]@{ 'decorator' = 'WorkflowGridDecorator'; 'selector' = 'CustomTemplateSelector' }

$configs = [ordered]@{
    WinForms = @{
        PackId          = 'VeloxDev.WinForms.Templates'
        TemplateProject = 'Src\Templates\VeloxDev.WinForms.Templates\working\VeloxDev.WinForms.Templates.csproj'
        AdapterProject  = 'Src\Adapters\VeloxDev.WinForms\VeloxDev.WinForms.csproj'
        MirrorRoot      = 'Examples\Workflow\WinForms Trimmed\Demo\Views\Workflow'
        MirrorNamespaces = @('Demo.Views.Workflow')
        ProbeNs         = 'Probe.Views'
        ProbeKind       = 'WinForms'
        MarkupExt       = $null
        RazorAllMarkup  = $false
        MirrorBase      = @{}
        Aliases         = [ordered]@{}
        Expected        = @{
            'tree' = 'the mirror carries the demo HUD'
            'link' = 'the mirror carries the demo-only hover highlight; Core, the adapters and the templates no longer paint one'
            'node' = 'the mirror sets card.NodeTitle with its own node type; the template cannot - NodeTitle is a Func<IWorkflowNodeViewModel, string> and the node''s name property belongs to the project, so the template ships that line commented out as guidance'
        }
        ProbeShell      = 'Microsoft.NET.Sdk'
        UseWPF          = $false
        UseWinForms     = $true
    }
    Jalium = @{
        PackId          = 'VeloxDev.Jalium.Templates'
        TemplateProject = 'Src\Templates\VeloxDev.Jalium.Templates\VeloxDev.Jalium.Templates.csproj'
        AdapterProject  = 'Src\Adapters\VeloxDev.Jalium\VeloxDev.Jalium.csproj'
        MirrorRoot      = 'Examples\Workflow\Jalium Trimmed\Demo\Views\Workflow'
        MirrorNamespaces = @('Demo.Views.Workflow')
        ProbeNs         = 'Probe.Views'
        ProbeKind       = 'Jalium'
        MarkupExt       = '.jalxaml'
        RazorAllMarkup  = $false
        MirrorBase      = @{}
        Aliases         = [ordered]@{}
        Expected        = @{
            'link' = 'the mirror carries the demo-only hover highlight; Core, the adapters and the templates no longer paint one'
            'tree' = 'the mirror carries the demo HUD and its own link-menu entries (delete is the host''s, the template ships none)'
        }
        ProbeShell      = 'Microsoft.NET.Sdk'
        UseWPF          = $false
        UseWinForms     = $false
    }
    WPF = @{
        PackId          = 'VeloxDev.WPF.Templates'
        TemplateProject = 'Src\Templates\VeloxDev.WPF.Templates\working\VeloxDev.WPF.Templates.csproj'
        AdapterProject  = 'Src\Adapters\VeloxDev.WPF\VeloxDev.WPF.csproj'
        MirrorRoot      = 'Examples\Workflow\WPF Trimmed\Demo\Views\Workflow'
        MirrorNamespaces = @('Demo.Views.Workflow', 'Demo.Views')
        ProbeNs         = 'Probe.Views'
        ProbeKind       = 'WPF'
        MarkupExt       = '.xaml'
        RazorAllMarkup  = $false
        MirrorBase      = $renameWpfBase
        Aliases         = $renameWpfFamily
        Expected        = @{
            'tree' = 'the mirror carries the demo HUD'
            'link' = 'the mirror carries the demo-only hover highlight; Core, the adapters and the templates no longer paint one'
        }
        ProbeShell      = 'Microsoft.NET.Sdk'
        UseWPF          = $true
        UseWinForms     = $false
    }
    Avalonia = @{
        PackId          = 'VeloxDev.Avalonia.Templates'
        TemplateProject = 'Src\Templates\VeloxDev.Avalonia.Templates\working\VeloxDev.Avalonia.Templates.csproj'
        AdapterProject  = 'Src\Adapters\VeloxDev.Avalonia\VeloxDev.Avalonia.csproj'
        MirrorRoot      = 'Examples\Workflow\Avalonia Trimmed\Demo\Demo\Views\Workflow'
        MirrorNamespaces = @('Demo')
        ProbeNs         = 'Probe.Views'
        ProbeKind       = 'Avalonia'
        MarkupExt       = '.axaml'
        RazorAllMarkup  = $false
        MirrorBase      = $renameWpfBase
        Aliases         = $renameWpfFamily
        Expected        = @{
            'tree'   = 'the mirror carries the demo HUD'
            'node'   = 'the mirror replaces the template x:CompileBindings=False with a concrete x:DataType, as the template comment instructs'
            'link'   = 'x:DataType in the mirror, plus the mirror carries the demo-only hover highlight; Core, the adapters and the templates no longer paint one'
        }
        ProbeShell      = 'Microsoft.NET.Sdk'
        UseWPF          = $false
        UseWinForms     = $false
        ExtraSources    = @{
            # The Avalonia tree template's compiled bindings take a concrete x:DataType (its own comment says the
            # build fails on vm:TreeViewModel until the consumer points it at their tree VM). The probe stands in
            # for that consumer, so it ships the minimal TreeViewModel the generated XAML binds through.
            'TreeViewModelStub.cs' = @'
using System.Collections.Generic;
using VeloxDev.WorkflowSystem;

namespace Probe.Views;

public sealed class TreeViewModel
{
    public LayoutProxy Layout { get; } = new();
    public HelperProxy Helper { get; } = new();
}

public sealed class LayoutProxy
{
    public SizeProxy ActualSize { get; } = new();
}

public sealed class SizeProxy
{
    public double Width => 0d;
    public double Height => 0d;
}

public sealed class HelperProxy
{
    public IEnumerable<IWorkflowNodeViewModel> VisibleItems { get; } = System.Array.Empty<IWorkflowNodeViewModel>();
}
'@
        }
    }
    WinUI = @{
        PackId          = 'VeloxDev.WinUI.Templates'
        TemplateProject = 'Src\Templates\VeloxDev.WinUI.Templates\working\VeloxDev.WinUI.Templates.csproj'
        AdapterProject  = 'Src\Adapters\VeloxDev.WinUI\VeloxDev.WinUI.csproj'
        MirrorRoot      = 'Examples\Workflow\WinUI Trimmed\Demo\Views\Workflow'
        MirrorNamespaces = @('Demo.Views.Workflow')
        ProbeNs         = 'Probe.Views'
        ProbeKind       = 'WinUI'
        MarkupExt       = '.xaml'
        RazorAllMarkup  = $false
        MirrorBase      = $renameWpfBase
        Aliases         = $renameWpfFamily
        Expected        = @{
            'tree' = 'the mirror carries the demo HUD'
            'link' = 'the mirror carries the demo-only hover highlight; Core, the adapters and the templates no longer paint one'
        }
        ProbeShell      = 'Microsoft.NET.Sdk'
        UseWPF          = $false
        UseWinForms     = $false
    }
    MAUI = @{
        PackId          = 'VeloxDev.MAUI.Templates'
        TemplateProject = 'Src\Templates\VeloxDev.MAUI.Templates\working\VeloxDev.MAUI.Templates.csproj'
        AdapterProject  = 'Src\Adapters\VeloxDev.MAUI\VeloxDev.MAUI.csproj'
        MirrorRoot      = 'Examples\Workflow\MAUI Trimmed\Demo\Controls\Workflow'
        MirrorNamespaces = @('Demo.Controls')
        ProbeNs         = 'Probe.Controls'
        ProbeKind       = 'MAUI'
        MarkupExt       = '.xaml'
        RazorAllMarkup  = $false
        MirrorBase      = $renameWpfBase
        Aliases         = $renameWpfFamily
        Expected        = @{
            'tree' = 'the mirror carries the demo HUD'
            'link' = 'the mirror carries the demo-only hover highlight; Core, the adapters and the templates no longer paint one'
        }
        ProbeShell      = 'Microsoft.NET.Sdk'
        UseWPF          = $false
        UseWinForms     = $false
    }
    Razor = @{
        PackId          = 'VeloxDev.Razor.Templates'
        TemplateProject = 'Src\Templates\VeloxDev.Razor.Templates\working\VeloxDev.Razor.Templates.csproj'
        AdapterProject  = 'Src\Adapters\VeloxDev.Razor\VeloxDev.Razor.csproj'
        MirrorRoot      = 'Examples\Workflow\Blazor Trimmed\Demo\Components\Workflow'
        MirrorNamespaces = @('Demo.Components.Workflow')
        ProbeNs         = 'Probe.Components.Workflow'
        ProbeKind       = 'Razor'
        MarkupExt       = '.razor'
        RazorAllMarkup  = $true
        MirrorBase      = @{ 'selector' = 'CustomTemplateSelector' }
        Aliases         = $renameRazorOnly
        Expected        = @{
            'tree' = 'the mirror carries the demo HUD'
            'link' = 'the mirror carries the demo-only hover highlight; Core, the adapters and the templates no longer paint one'
        }
        ProbeShell      = 'Microsoft.NET.Sdk.Web'
        UseWPF          = $false
        UseWinForms     = $false
    }
}

function Invoke-Step([string] $what, [scriptblock] $body) {
    Write-Host "  $what" -ForegroundColor DarkGray
    & $body
    if ($LASTEXITCODE -ne 0) { throw "$what failed (exit $LASTEXITCODE)" }
}

# Uninstalling a pack that was never installed is not an error, but dotnet writes to stderr for it and
# $ErrorActionPreference = 'Stop' turns that into a terminating NativeCommandError.
function Uninstall-TemplatePack([string] $packId) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { dotnet new uninstall $packId 2>&1 | Out-Null } catch { } finally { $ErrorActionPreference = $previous }
}

function Get-ItemFiles($config, [string] $key) {
    if ($config.RazorAllMarkup -or $itemDefs[$key].Kind -eq 'markup') {
        if (-not $config.MarkupExt) { return @('.cs') }
        return @($config.MarkupExt, ($config.MarkupExt + '.cs'))
    }
    return @('.cs')
}

function Get-CliShortName($config, [string] $key) {
    $prefix = switch ($config.ProbeKind) {
        'WinForms' { 'winforms' }
        'Jalium'   { 'jalium' }
        'WPF'      { 'wpf' }
        'Avalonia' { 'ava' }
        'WinUI'    { 'winui' }
        'MAUI'     { 'maui' }
        'Razor'    { 'razor' }
    }
    return "$prefix-v-$key"
}

# New-ProbeProject writes the minimal shell that can compile the generated items against the adapter. Every shell is
# modelled on the checked-in trimmed demo's csproj, minus the app entry point and the demo's own assets.
function New-ProbeProject($config, [string] $probeDir) {
    $adapter = Join-Path $repoRoot $config.AdapterProject
    $shell = $config.ProbeShell

    switch ($config.ProbeKind) {
        'WinForms' {
            $project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Library</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWindowsForms>true</UseWindowsForms>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$adapter" />
  </ItemGroup>
</Project>
"@
        }
        'WPF' {
            $project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Library</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$adapter" />
  </ItemGroup>
</Project>
"@
        }
        'Jalium' {
            $project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Library</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Jalium.UI.Desktop" Version="26.10.8" />
    <ProjectReference Include="$adapter" />
  </ItemGroup>
</Project>
"@
        }
        'Avalonia' {
            $project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Library</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Avalonia" Version="12.0.3" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="12.0.3" />
    <ProjectReference Include="$adapter" />
  </ItemGroup>
</Project>
"@
        }
        'WinUI' {
            $project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Library</OutputType>
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <TargetPlatformMinVersion>10.0.17763.0</TargetPlatformMinVersion>
    <UseWinUI>true</UseWinUI>
    <WindowsPackageType>None</WindowsPackageType>
    <WinUISDKReferences>false</WinUISDKReferences>
    <EnableMsixTooling>false</EnableMsixTooling>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <Nullable>enable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Windows.SDK.BuildTools" Version="10.0.26100.6584" />
    <PackageReference Include="Microsoft.WindowsAppSDK" Version="1.8.251003001" />
    <ProjectReference Include="$adapter">
      <SetTargetFramework>TargetFramework=net10.0-windows10.0.19041.0</SetTargetFramework>
    </ProjectReference>
  </ItemGroup>
</Project>
"@
        }
        'MAUI' {
            $project = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Library</OutputType>
    <TargetFramework>net10.0-windows10.0.19041.0</TargetFramework>
    <RootNamespace>Probe</RootNamespace>
    <UseMaui>true</UseMaui>
    <SingleProject>true</SingleProject>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <NoWarn>CA1416</NoWarn>
    <MauiVersion>10.0.20</MauiVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Maui.Controls" Version="`$(MauiVersion)" />
    <ProjectReference Include="$adapter" />
  </ItemGroup>
</Project>
"@
        }
        'Razor' {
            $project = @"
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <OutputType>Library</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$adapter" />
  </ItemGroup>
</Project>
"@
        }
    }

    Set-Content -Path (Join-Path $probeDir 'Probe.csproj') -Value $project -Encoding UTF8

    if ($config.Contains('ExtraSources')) {
        foreach ($file in $config.ExtraSources.Keys) {
            Set-Content -Path (Join-Path $probeDir $file) -Value $config.ExtraSources[$file] -Encoding UTF8
        }
    }
}

# Reads every `replaces` token declared by this platform's item templates. A token surviving into a generated file
# means a symbol the template references is not defined (the Jalium tree's `TemplateLinkColor`) — the round-trip
# silently leaves the literal in the user's code, which is invisible to a comparison that normalises colours.
function Get-PlaceholderTokens($config) {
    $tokens = New-Object System.Collections.Generic.HashSet[string]
    $projectDir = Split-Path (Join-Path $repoRoot $config.TemplateProject)
    $configs = @(Get-ChildItem -Path $projectDir -Recurse -Filter 'template.json' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\content\\' -and $_.FullName -notmatch '\\bin\\|\\obj\\' })
    foreach ($file in $configs) {
        $json = Get-Content $file.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
        foreach ($prop in $json.symbols.PSObject.Properties) {
            $replaces = $prop.Value.replaces
            if ($replaces) { [void]$tokens.Add([string]$replaces) }
        }
    }
    return $tokens
}

function Assert-NoPlaceholders([string] $probeDir, $tokens) {
    $views = Join-Path $probeDir 'Views'
    $hits = New-Object System.Collections.ArrayList
    foreach ($file in @(Get-ChildItem $views -Recurse -File)) {
        $text = Get-Content $file.FullName -Raw -Encoding UTF8
        foreach ($token in $tokens) {
            if ($text -match ('\b' + [regex]::Escape($token) + '\b')) { [void]$hits.Add("$($file.Name) contains $token") }
        }
    }
    if ($hits.Count -gt 0) { throw ("un-substituted template placeholder(s): " + ($hits -join '; ')) }
}

# Normalisation: the mirror is allowed to root its namespace in the demo, rename sibling items the demo renamed,
# spell symbol values instead of parsing them, carry the demo-only HUD, and drop the generation-time header comment.
# Everything else is compared line-for-line.
function Normalize-Text([string] $text, [string[]] $namespaces, [System.Collections.IDictionary] $aliases, [string] $kind) {
    $text = $text -replace "`r", ''
    foreach ($ns in $namespaces) {
        $pattern = '(?<![\w.])' + [regex]::Escape($ns) + '(?![\w])'
        $text = [regex]::Replace($text, $pattern, '<ns>')
    }
    foreach ($from in $aliases.Keys) {
        $text = [regex]::Replace($text, '\b' + [regex]::Escape($from) + '\b', [string]$aliases[$from])
    }
    # Jalium's template spells a colour as a string the parser converts; the mirror spells the same colour as a
    # factory call. Collapse both so the comparison sees the shape, not the spelling (as the dedicated Jalium
    # script did before this one subsumed it).
    if ($kind -eq 'Jalium') {
        $text = [regex]::Replace($text, 'ColorConverter\.ConvertFromString\("[^"]*"\)', '<color>')
        $text = [regex]::Replace($text, 'Color\.FromArgb\([^()]*\)', '<color>')
        $text = [regex]::Replace($text, 'Color\.FromRgb\([^()]*\)', '<color>')
        $text = [regex]::Replace($text, 'Colors\.\w+', '<color>')
        $text = $text.Replace('(Color)<color>', '<color>')
    }
    # MAUI's mirror writes Colors.White where the template writes Color.FromArgb("#FFFFFFFF") — the same colour
    # spelled two ways. Collapse both.
    if ($kind -eq 'MAUI') {
        $text = [regex]::Replace($text, 'Color\.FromArgb\("[^"]*"\)', '<color>')
        $text = [regex]::Replace($text, 'Colors\.\w+', '<color>')
    }
    # A double written 2 vs 2.0 is the same value; only word-bounded literals are touched so net10.0 etc. survive.
    $text = [regex]::Replace($text, '(?<![\w.])(\d+)\.0(?![\w.])', '$1')
    return $text
}

# Removes the generation-time instruction line ("generate the other six templates …") which the demo has no reason
# to carry.
function Remove-HeaderLines([string[]] $lines) {
    return @($lines | Where-Object { $_ -notmatch 'Generate the Node, Slot, Link, selector, minimap' })
}

# Change-only line diff (LCS). A positional walk would read one inserted comment as every following line drifting.
function Get-LineDiff([string[]] $gen, [string[]] $mir) {
    $n = $gen.Count
    $m = $mir.Count
    $w = $m + 1
    $dp = New-Object 'int[]' (($n + 1) * $w)
    for ($i = $n - 1; $i -ge 0; $i--) {
        $ii = $i + 1
        for ($j = $m - 1; $j -ge 0; $j--) {
            $jj = $j + 1
            $here = $i * $w + $j
            if ($gen[$i] -ceq $mir[$j]) { $dp[$here] = $dp[$ii * $w + $jj] + 1 }
            else {
                $down = $dp[$ii * $w + $j]
                $right = $dp[$i * $w + $jj]
                $dp[$here] = [Math]::Max($down, $right)
            }
        }
    }
    $ops = New-Object System.Collections.ArrayList
    $i = 0; $j = 0
    while ($i -lt $n -and $j -lt $m) {
        $ii = $i + 1
        $jj = $j + 1
        if ($gen[$i] -ceq $mir[$j]) { [void]$ops.Add([pscustomobject]@{ Kind = 'same'; Gen = $gen[$i]; Mir = $mir[$j]; GenLine = $ii; MirLine = $jj }); $i++; $j++ }
        elseif ($dp[$ii * $w + $j] -ge $dp[$i * $w + $jj]) { [void]$ops.Add([pscustomobject]@{ Kind = 'del'; Gen = $gen[$i]; Mir = $null; GenLine = $ii; MirLine = $null }); $i++ }
        else { [void]$ops.Add([pscustomobject]@{ Kind = 'add'; Gen = $null; Mir = $mir[$j]; GenLine = $null; MirLine = $jj }); $j++ }
    }
    while ($i -lt $n) { [void]$ops.Add([pscustomobject]@{ Kind = 'del'; Gen = $gen[$i]; Mir = $null; GenLine = $i + 1; MirLine = $null }); $i++ }
    while ($j -lt $m) { [void]$ops.Add([pscustomobject]@{ Kind = 'add'; Gen = $null; Mir = $mir[$j]; GenLine = $null; MirLine = $j + 1 }); $j++ }
    return $ops
}

# Drops comment-only and blank lines from both sides: the mirror is a demo, its prose (and whether it carries the
# template's "VeloxDev customization:" guidance comments at all) is its own. Code and markup are what must match.
function Remove-CommentLines([string[]] $lines) {
    $out = New-Object System.Collections.ArrayList
    $inBlock = $false
    foreach ($line in $lines) {
        $t = $line.Trim()
        if ($inBlock) {
            if ($t -match '\-\->' -or $t -match '\*@' -or $t -match '\*/') { $inBlock = $false }
            continue
        }
        if ($t -eq '') { continue }
        if ($t.StartsWith('<!--')) { if (-not $t.Contains('-->')) { $inBlock = $true }; continue }
        if ($t.StartsWith('@*'))   { if (-not $t.Contains('*@'))  { $inBlock = $true }; continue }
        if ($t.StartsWith('/*'))   { if (-not $t.Contains('*/'))  { $inBlock = $true }; continue }
        if ($t.StartsWith('//'))   { continue }
        if ($t.StartsWith('*'))    { continue }
        if ($t.StartsWith('#region') -or $t.StartsWith('#endregion')) { continue }
        [void]$out.Add($line)
    }
    return $out.ToArray()
}

function Get-Squished([string[]] $lines) {
    return (($lines -join "`n") -replace '\s', '')
}

function Compare-File([string] $generatedPath, [string] $mirrorPath, [string] $probeNs, $config) {
    $genText = Normalize-Text ((Get-Content $generatedPath -Raw -Encoding UTF8)) @($probeNs) ([ordered]@{}) $config.ProbeKind
    $mirText = Normalize-Text ((Get-Content $mirrorPath -Raw -Encoding UTF8)) $config.MirrorNamespaces $config.Aliases $config.ProbeKind

    $genLines = Remove-CommentLines (Remove-HeaderLines ($genText -split "`n"))
    $mirLines = Remove-CommentLines (Remove-HeaderLines ($mirText -split "`n"))
    $ops = Get-LineDiff $genLines $mirLines
    $changes = @($ops | Where-Object { $_.Kind -ne 'same' })

    $formattingOnly = ($changes.Count -gt 0) -and ((Get-Squished $genLines) -ceq (Get-Squished $mirLines))

    return [pscustomobject]@{
        Match    = ($changes.Count -eq 0)
        FormattingOnly = $formattingOnly
        Changes  = $changes
    }
}

function Show-Hunk($result, [string] $colour) {
    foreach ($c in $result.Changes) {
        if ($c.Kind -eq 'del') { Write-Host ("    gen:{0,4} | {1}" -f $c.GenLine, $c.Gen) -ForegroundColor $colour }
        if ($c.Kind -eq 'add') { Write-Host ("    mir:{0,4} | {1}" -f $c.MirLine, $c.Mir) -ForegroundColor $colour }
    }
}

$summary = @()
$failedPlatforms = @()

foreach ($name in $Platform) {
    $config = $configs[$name]
    Write-Host ''
    Write-Host "===== $name =====" -ForegroundColor Cyan

    $probe = Join-Path ([System.IO.Path]::GetTempPath()) ("veloxdev-probe-$($config.ProbeKind.ToLower())-" + [guid]::NewGuid().ToString('N'))
    $nupkgs = Join-Path $probe 'nupkgs'
    $templateProject = Join-Path $repoRoot $config.TemplateProject
    $mirrorRoot = Join-Path $repoRoot $config.MirrorRoot

    $result = [ordered]@{ Platform = $name; Items = @(); Generated = 0; Built = $false; Matches = 0; Differs = 0; Expected = 0; Comments = 0; Error = $null }

    try {
        New-Item -ItemType Directory -Force -Path $probe, $nupkgs, (Join-Path $probe 'Views') | Out-Null

        Write-Host 'Packing and installing the template pack' -ForegroundColor Cyan
        Invoke-Step 'pack' { dotnet pack $templateProject -c Debug -o $nupkgs | Out-Null }
        $nupkg = Get-ChildItem $nupkgs -Filter '*.nupkg' | Select-Object -First 1
        if (-not $nupkg) { throw "no .nupkg was produced in $nupkgs" }

        Uninstall-TemplatePack $config.PackId
        Invoke-Step 'install' { dotnet new install $nupkg.FullName | Out-Null }

        Write-Host 'Writing the probe shell' -ForegroundColor Cyan
        New-ProbeProject $config $probe

        Write-Host 'Generating into the throwaway project' -ForegroundColor Cyan
        Push-Location $probe
        try {
            foreach ($key in $itemOrder) {
                $shortName = Get-CliShortName $config $key
                $generated = $itemDefs[$key].Generated
                Invoke-Step "generate $shortName" {
                    dotnet new $shortName -n $generated -ns $config.ProbeNs -o Views | Out-Null
                }
                $result.Generated++
            }
        }
        finally {
            Pop-Location
        }

        Assert-NoPlaceholders $probe (Get-PlaceholderTokens $config)

        Write-Host 'Building the generated set' -ForegroundColor Cyan
        # NuGet/MSBuild restore on a cold temp probe intermittently fails under file-lock pressure; retry before
        # believing it, and surface the real errors instead of swallowing them.
        $buildOutput = $null
        $built = $false
        for ($attempt = 1; $attempt -le 3 -and -not $built; $attempt++) {
            Write-Host "  build (attempt $attempt)" -ForegroundColor DarkGray
            $buildOutput = dotnet build (Join-Path $probe 'Probe.csproj') -c Debug 2>&1
            if ($LASTEXITCODE -eq 0) { $built = $true } else { Start-Sleep -Seconds 5 }
        }
        if (-not $built) {
            Write-Host (($buildOutput | Select-Object -Last 40) -join "`n")
            throw 'build failed'
        }
        $result.Built = $true

        Write-Host 'Comparing each generated file against its mirror' -ForegroundColor Cyan
        foreach ($key in $itemOrder) {
            $generatedBase = $itemDefs[$key].Generated
            $mirrorBase = if ($config.MirrorBase.Contains($key)) { $config.MirrorBase[$key] } else { $generatedBase }
            $files = Get-ItemFiles $config $key
            $expectedReason = if ($config.Expected.Contains($key)) { $config.Expected[$key] } else { $null }

            foreach ($ext in $files) {
                $generatedPath = Join-Path $probe "Views\$generatedBase$ext"
                $mirrorPath = Join-Path $mirrorRoot "$mirrorBase$ext"
                $label = "$generatedBase$ext"

                if (-not (Test-Path $generatedPath)) { throw "$key produced no $label" }
                if (-not (Test-Path $mirrorPath)) { Write-Warning "no mirror at $mirrorPath"; continue }

                $cmp = Compare-File $generatedPath $mirrorPath $config.ProbeNs $config

                if ($cmp.Match) {
                    Write-Host "  $label matches" -ForegroundColor Green
                    $result.Items += [pscustomobject]@{ File = $label; Status = 'match' }
                }
                elseif ($expectedReason) {
                    Write-Host "  $label differs (expected: $expectedReason)" -ForegroundColor Yellow
                    Show-Hunk $cmp 'Yellow'
                    $result.Items += [pscustomobject]@{ File = $label; Status = 'expected' }
                }
                elseif ($cmp.FormattingOnly) {
                    Write-Host "  $label differs in formatting only (allowed)" -ForegroundColor DarkGray
                    $result.Items += [pscustomobject]@{ File = $label; Status = 'formatting' }
                }
                else {
                    Write-Host "  $label DIFFERS" -ForegroundColor Red
                    Show-Hunk $cmp 'Red'
                    $result.Items += [pscustomobject]@{ File = $label; Status = 'drift' }
                }
            }
        }

        $result.Matches = @($result.Items | Where-Object { $_.Status -eq 'match' }).Count
        $result.Differs = @($result.Items | Where-Object { $_.Status -eq 'drift' }).Count
        $result.Expected = @($result.Items | Where-Object { $_.Status -eq 'expected' }).Count
        $result.Comments = @($result.Items | Where-Object { $_.Status -eq 'formatting' }).Count
    }
    catch {
        $result.Error = $_.Exception.Message
        $failedPlatforms += $name
        Write-Host "  ERROR: $($result.Error)" -ForegroundColor Red
    }
    finally {
        Uninstall-TemplatePack $config.PackId
        if ($KeepProbe) { Write-Host "probe kept at $probe" } else { Remove-Item -Recurse -Force $probe -ErrorAction SilentlyContinue }
    }

    $summary += $result
}

Write-Host ''
Write-Host '===== Summary =====' -ForegroundColor Cyan
foreach ($r in $summary) {
    if ($r.Error) {
        Write-Host ("{0,-9} ERROR  {1}" -f $r.Platform, $r.Error) -ForegroundColor Red
    }
    else {
        $colour = if ($r.Differs -gt 0) { 'Red' } elseif (($r.Expected + $r.Comments) -gt 0) { 'Yellow' } else { 'Green' }
        Write-Host ("{0,-9} generated {1}/7  built {2}  match {3}  formatting {4}  expected-diff {5}  drift {6}" -f $r.Platform, $r.Generated, $r.Built, $r.Matches, $r.Comments, $r.Expected, $r.Differs) -ForegroundColor $colour
    }
}

$totalDrift = @($summary | Where-Object { $_.Differs -gt 0 }).Count
$totalErrors = @($summary | Where-Object { $_.Error }).Count

if ($totalErrors -gt 0) {
    Write-Host "$totalErrors platform(s) could not be generated or built" -ForegroundColor Red
    exit 3
}
if ($totalDrift -gt 0) {
    $message = "$totalDrift platform(s) have real template/mirror drift"
    if ($Strict) { throw $message }
    Write-Warning "$message — pass -Strict to fail on this"
    exit 2
}

Write-Host 'OK' -ForegroundColor Green
