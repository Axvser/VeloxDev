# Package plan

Run every block from the repository root (the directory containing
`Src/Core/VeloxDev.Core/VeloxDev.Core.csproj`), in the same PowerShell session. The blocks below
assume `$version`, `$repo`, `$staging`, `$dependentProjects`, `$templateProjects`,
`$restoreSource` and `$pushSource` are already defined by the earlier blocks:

```powershell
$version = "10.2.0"
$repo = (Get-Location).Path
$staging = Join-Path $env:TEMP "veloxdev-release-$version"
$restoreSource = "https://www.nuget.org/api/v2/"
$pushSource = "https://api.nuget.org/v3/index.json"
```

## The 16 packages

| Kind | Id | Project |
| --- | --- | --- |
| Core | `VeloxDev.Core` | `Src/Core/VeloxDev.Core/VeloxDev.Core.csproj` |
| Extension | `VeloxDev.Core.Extension` | `Src/Core/VeloxDev.Core.Extension/VeloxDev.Core.Extension.csproj` |
| Adapter | `VeloxDev.WPF` | `Src/Adapters/VeloxDev.WPF/VeloxDev.WPF.csproj` |
| Adapter | `VeloxDev.Avalonia` | `Src/Adapters/VeloxDev.Avalonia/VeloxDev.Avalonia.csproj` |
| Adapter | `VeloxDev.WinUI` | `Src/Adapters/VeloxDev.WinUI/VeloxDev.WinUI.csproj` |
| Adapter | `VeloxDev.MAUI` | `Src/Adapters/VeloxDev.MAUI/VeloxDev.MAUI.csproj` |
| Adapter | `VeloxDev.Razor` | `Src/Adapters/VeloxDev.Razor/VeloxDev.Razor.csproj` |
| Adapter | `VeloxDev.WinForms` | `Src/Adapters/VeloxDev.WinForms/VeloxDev.WinForms.csproj` |
| Adapter | `VeloxDev.Jalium` | `Src/Adapters/VeloxDev.Jalium/VeloxDev.Jalium.csproj` |
| Template | `VeloxDev.WPF.Templates` | `Src/Templates/VeloxDev.WPF.Templates/working/VeloxDev.WPF.Templates.csproj` |
| Template | `VeloxDev.Avalonia.Templates` | `Src/Templates/VeloxDev.Avalonia.Templates/working/VeloxDev.Avalonia.Templates.csproj` |
| Template | `VeloxDev.WinUI.Templates` | `Src/Templates/VeloxDev.WinUI.Templates/working/VeloxDev.WinUI.Templates.csproj` |
| Template | `VeloxDev.MAUI.Templates` | `Src/Templates/VeloxDev.MAUI.Templates/working/VeloxDev.MAUI.Templates.csproj` |
| Template | `VeloxDev.Razor.Templates` | `Src/Templates/VeloxDev.Razor.Templates/working/VeloxDev.Razor.Templates.csproj` |
| Template | `VeloxDev.WinForms.Templates` | `Src/Templates/VeloxDev.WinForms.Templates/working/VeloxDev.WinForms.Templates.csproj` |
| Template | `VeloxDev.Jalium.Templates` | `Src/Templates/VeloxDev.Jalium.Templates/VeloxDev.Jalium.Templates.csproj` |

The seven adapters and the Extension are the only projects that carry a
`<PackageReference Include="VeloxDev.Core" Version="…" />`. The templates have a direct
`<Version>` but no Core reference. The Core project itself is never a dependent.

Representative packages re-downloaded after a publish: `VeloxDev.Core`,
`VeloxDev.Core.Extension`, `VeloxDev.WPF`, `VeloxDev.WPF.Templates`.

## Bump

This rewrites the literal versions in the projects. It is the only version source; do not add a
`VeloxDevCoreVersion` property.

```powershell
$version = "10.2.0"          # the version confirmed with the developer
$repo = (Get-Location).Path

$coreProjects = @(
    "Src/Core/VeloxDev.Core/VeloxDev.Core.csproj"
)

$dependentProjects = @(
    "Src/Core/VeloxDev.Core.Extension/VeloxDev.Core.Extension.csproj",
    "Src/Adapters/VeloxDev.WPF/VeloxDev.WPF.csproj",
    "Src/Adapters/VeloxDev.Avalonia/VeloxDev.Avalonia.csproj",
    "Src/Adapters/VeloxDev.WinUI/VeloxDev.WinUI.csproj",
    "Src/Adapters/VeloxDev.MAUI/VeloxDev.MAUI.csproj",
    "Src/Adapters/VeloxDev.Razor/VeloxDev.Razor.csproj",
    "Src/Adapters/VeloxDev.WinForms/VeloxDev.WinForms.csproj",
    "Src/Adapters/VeloxDev.Jalium/VeloxDev.Jalium.csproj"
)

$templateProjects = @(
    "Src/Templates/VeloxDev.WPF.Templates/working/VeloxDev.WPF.Templates.csproj",
    "Src/Templates/VeloxDev.Avalonia.Templates/working/VeloxDev.Avalonia.Templates.csproj",
    "Src/Templates/VeloxDev.WinUI.Templates/working/VeloxDev.WinUI.Templates.csproj",
    "Src/Templates/VeloxDev.MAUI.Templates/working/VeloxDev.MAUI.Templates.csproj",
    "Src/Templates/VeloxDev.Razor.Templates/working/VeloxDev.Razor.Templates.csproj",
    "Src/Templates/VeloxDev.WinForms.Templates/working/VeloxDev.WinForms.Templates.csproj",
    "Src/Templates/VeloxDev.Jalium.Templates/VeloxDev.Jalium.Templates.csproj"
)

foreach ($relative in ($coreProjects + $dependentProjects + $templateProjects)) {
    $path = Join-Path $repo $relative
    if (-not (Test-Path $path)) { throw "missing $relative" }

    $text = [IO.File]::ReadAllText($path)
    $next = [regex]::Replace($text, '<Version>[^<]+</Version>', "<Version>$version</Version>")
    if ($next -notmatch "<Version>$([regex]::Escape($version))</Version>") {
        throw "no <Version> element in $relative"
    }

    if ($dependentProjects -contains $relative) {
        $next = [regex]::Replace(
            $next,
            '(Include="VeloxDev\.Core"\s+Version=")[^"]+(")',
            '${1}' + $version + '${2}')
        if ($next -notmatch ('Include="VeloxDev\.Core"\s+Version="' + [regex]::Escape($version) + '"')) {
            throw "no rewritable VeloxDev.Core reference in $relative"
        }
    }

    if ($next -ne $text) {
        [IO.File]::WriteAllText($path, $next, [Text.UTF8Encoding]::new($false))
    }
}
```

Verify with `git diff -- Src` before packing. The diff should be exactly the 16 `<Version>` lines and
the 8 Core-reference lines.

## Core package and its TFM check

```powershell
$staging = Join-Path $env:TEMP "veloxdev-release-$version"
New-Item -ItemType Directory -Force $staging | Out-Null

dotnet clean "Src/Core/VeloxDev.Core/VeloxDev.Core.csproj" -c Release
dotnet pack "Src/Core/VeloxDev.Core/VeloxDev.Core.csproj" -c Release -o $staging `
    -p:Version=$version -p:PackageVersion=$version

$nupkg = Join-Path $staging "VeloxDev.Core.$version.nupkg"
$inspect = Join-Path $staging "core-inspect"
Remove-Item $inspect -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item $nupkg "$nupkg.zip" -Force
Expand-Archive "$nupkg.zip" $inspect -Force
$lib = Join-Path $inspect "lib"
if (-not (Test-Path $lib)) { throw "Core package has no lib/ directory" }
$tfms = Get-ChildItem $lib -Directory | Select-Object -ExpandProperty Name
if (-not $tfms) { throw "Core package has no lib/<tfm> assemblies" }
"Core TFMs: $($tfms -join ', ')"
```

## Dependent build (Release against the working-tree Core)

`Src/Directory.Build.targets` swaps `VeloxDev.Core` from a package reference to a project reference
when `VeloxDevLocalCore=true`. That is what makes a build-only run possible before the new Core is
on NuGet, without a second version property in the csproj files.

```powershell
foreach ($relative in $dependentProjects) {
    $project = Join-Path $repo $relative
    dotnet restore $project -p:Configuration=Release -p:VeloxDevLocalCore=true --source $restoreSource
    dotnet build $project -c Release --no-restore `
        -p:Version=$version -p:PackageVersion=$version -p:VeloxDevLocalCore=true

    $dir = Split-Path $project
    $nupkg = Get-ChildItem (Join-Path $dir "bin/Release") "*.nupkg" |
        Where-Object { $_.Name -like "*.$version.nupkg" } |
        Select-Object -First 1
    if (-not $nupkg) { throw "no nupkg produced for $relative" }
    Copy-Item $nupkg.FullName $staging -Force
}
```

The Extension and all seven adapters have `GeneratePackageOnBuild`, so the build writes the nupkg.
The seven template projects do not; pack them:

```powershell
foreach ($relative in $templateProjects) {
    dotnet pack (Join-Path $repo $relative) -c Release -o $staging `
        -p:Version=$version -p:PackageVersion=$version
}
```

## Publish

Only after explicit confirmation. `$pushSource` defaults to
`https://api.nuget.org/v3/index.json`.

```powershell
dotnet nuget push (Join-Path $staging "VeloxDev.Core.$version.nupkg") `
    --api-key $env:NUGET_API_KEY --source $pushSource --skip-duplicate

$deadline = (Get-Date).AddMinutes(30)
while ($true) {
    try {
        $response = Invoke-WebRequest `
            -Uri "https://www.nuget.org/api/v2/package/VeloxDev.Core/$version" `
            -Method Get -UseBasicParsing -TimeoutSec 30
        if ($response.StatusCode -eq 200) { break }
    } catch { }
    if ((Get-Date) -gt $deadline) { throw "Core did not appear within 30 minutes" }
    Start-Sleep -Seconds 20
}
```

After the Core is available, build the dependents **without** `VeloxDevLocalCore` (restore from
v2, so the new Core resolves), stage each nupkg, and push Core's dependents before the templates:

```powershell
foreach ($relative in $dependentProjects) {
    $project = Join-Path $repo $relative
    dotnet restore $project -p:Configuration=Release --source $restoreSource
    dotnet build $project -c Release --no-restore -p:Version=$version -p:PackageVersion=$version

    $nupkg = Get-ChildItem (Join-Path (Split-Path $project) "bin/Release") "*.nupkg" |
        Where-Object { $_.Name -like "*.$version.nupkg" } |
        Select-Object -First 1
    if (-not $nupkg) { throw "no nupkg produced for $relative" }
    $staged = Join-Path $staging $nupkg.Name
    Copy-Item $nupkg.FullName $staged -Force
    dotnet nuget push $staged --api-key $env:NUGET_API_KEY --source $pushSource --skip-duplicate
}

foreach ($relative in $templateProjects) {
    dotnet pack (Join-Path $repo $relative) -c Release -o $staging `
        -p:Version=$version -p:PackageVersion=$version
    $id = [IO.Path]::GetFileNameWithoutExtension($relative)
    dotnet nuget push (Join-Path $staging "$id.$version.nupkg") `
        --api-key $env:NUGET_API_KEY --source $pushSource --skip-duplicate
}
```

## Verify

```powershell
$verify = Join-Path $staging "verify"
New-Item -ItemType Directory -Force $verify | Out-Null

foreach ($id in @('VeloxDev.Core', 'VeloxDev.Core.Extension', 'VeloxDev.WPF', 'VeloxDev.WPF.Templates')) {
    Invoke-WebRequest "https://www.nuget.org/api/v2/package/$id/$version" `
        -OutFile (Join-Path $verify "$id.$version.nupkg") -UseBasicParsing
}

dotnet new install (Join-Path $verify "VeloxDev.WPF.Templates.$version.nupkg") `
    --debug:custom-hive (Join-Path $verify "hive")
```

## Unlist old versions — only on explicit request

Do not run this unless the developer explicitly asks to unlist old versions. Keep the newest N
(default 2) and never unlist the version this run publishes:

```powershell
$keep = 2
$oldVersions = @('10.0.9', '10.1.0')   # replace with the versions the developer approved
foreach ($id in @('VeloxDev.Core', 'VeloxDev.Core.Extension')) {
    foreach ($old in $oldVersions) {
        if ($old -eq $version) { continue }
        dotnet nuget delete $id $old --api-key $env:NUGET_API_KEY `
            --source $pushSource --non-interactive
    }
}
```

NuGet keeps the package downloadable by exact version after an unlist; this is not a delete.

## Report

```text
| Package | Version | Status |
| --- | --- | --- |
| VeloxDev.Core | 10.2.0 | pushed / verified |
| ... | ... | ... |
```

Report every package, the staging directory, whether the version bump is still uncommitted, and
the first failure if the run stopped. Nothing is unlisted unless the developer explicitly asked.
