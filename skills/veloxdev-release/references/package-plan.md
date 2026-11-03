# Package plan

Run every block from the repository root (the directory containing
`Src/Core/VeloxDev.Core/VeloxDev.Core.csproj`), in the same PowerShell session. The blocks below
assume `$version`, `$repo`, `$staging`, `$dependentProjects`, `$templateProjects`,
`$restoreSource` and `$pushSource` are already defined by the earlier blocks:

```powershell
$version = "11.0.0"
$repo = (Get-Location).Path
$staging = Join-Path $env:TEMP "veloxdev-release-$version"
$restoreSource = "https://www.nuget.org/api/v2/"
$pushSource = "https://api.nuget.org/v3/index.json"
```

## The 18 packages

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
| Dependency | `VeloxDev.Core.Generator` | `Src/Generators/VeloxDev.Core.Generator/VeloxDev.Core.Generator.csproj` |
| Tool | `VeloxDev.Cli` | `Src/Cli/VeloxDev.Cli/VeloxDev.Cli.csproj` |

The seven adapters and the Extension are the only projects that carry a
`<PackageReference Include="VeloxDev.Core" Version="…" />`. The templates, generator and CLI have a
direct `<Version>` but no Core reference. The Core project itself is never a dependent.

⚙ **The generator is a dependency, not a separate release.** Core and Extension reference
`VeloxDev.Core.Generator` at `$version`, so bump it in the same pass and publish it **before** Core
and Extension. The dependents can be built locally first because `VeloxDevLocalCore=true` now swaps
both Core and the generator to project references.

⚙ **The CLI is a .NET tool, counted in the release.** Pack it with `-p:VeloxDevLocalCore=true`; the
package bundles Core/Extension/MCP in `tools/net8.0/any/`, so it can be pushed after the library
packages. Verify it by installing the staged nupkg as a tool and running `veloxdev --help`.

Representative packages re-downloaded after a publish: `VeloxDev.Core`, `VeloxDev.Core.Extension`,
`VeloxDev.Core.Generator`, `VeloxDev.WPF`, `VeloxDev.WPF.Templates`, `VeloxDev.Cli`.

## Bump

This rewrites the literal versions in the projects. It is the only version source; do not add a
`VeloxDevCoreVersion` property.

```powershell
$version = "11.0.0"          # the version confirmed with the developer
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

$generatorProjects = @(
    "Src/Generators/VeloxDev.Core.Generator/VeloxDev.Core.Generator.csproj"
)

$toolProjects = @(
    "Src/Cli/VeloxDev.Cli/VeloxDev.Cli.csproj"
)

foreach ($relative in ($coreProjects + $dependentProjects + $templateProjects + $generatorProjects + $toolProjects)) {
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

Verify with `git diff -- Src` before packing. The diff should be exactly the 18 `<Version>` lines and
the 8 Core-reference lines.

## Generator and Core packages

The generator must exist before Core can be restored by a consumer, so pack and publish it first.
The Core pack uses `VeloxDevLocalCore=true` so its generator package reference resolves to the
working-tree project.

```powershell
$staging = Join-Path $env:TEMP "veloxdev-release-$version"
New-Item -ItemType Directory -Force $staging | Out-Null

# Both projects set GeneratePackageOnBuild=true, which strips Build from GenerateNuspecDependsOn:
# a bare pack would ship a DLL that was never built. Build first, then pack the built output.
dotnet build "Src/Generators/VeloxDev.Core.Generator/VeloxDev.Core.Generator.csproj" -c Release `
    -p:Version=$version -p:PackageVersion=$version -p:GeneratePackageOnBuild=false
dotnet pack "Src/Generators/VeloxDev.Core.Generator/VeloxDev.Core.Generator.csproj" -c Release -o $staging --no-build `
    -p:Version=$version -p:PackageVersion=$version

dotnet clean "Src/Core/VeloxDev.Core/VeloxDev.Core.csproj" -c Release
dotnet build "Src/Core/VeloxDev.Core/VeloxDev.Core.csproj" -c Release `
    -p:Version=$version -p:PackageVersion=$version -p:VeloxDevLocalCore=true -p:GeneratePackageOnBuild=false
dotnet pack "Src/Core/VeloxDev.Core/VeloxDev.Core.csproj" -c Release -o $staging --no-build `
    -p:Version=$version -p:PackageVersion=$version -p:VeloxDevLocalCore=true

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

# The CLI is a .NET tool and bundles Core/Extension; it needs the working-tree Core to pack.
dotnet pack "Src/Cli/VeloxDev.Cli/VeloxDev.Cli.csproj" -c Release -o $staging `
    -p:Version=$version -p:PackageVersion=$version -p:VeloxDevLocalCore=true
```

## Publish

Publishing is the GitHub Actions workflow `.github/workflows/publish.yml` through NuGet Trusted
Publishing. **Do not push any package from this local session.** The workflow:

- reads the version from `Src/Core/VeloxDev.Core/VeloxDev.Core.csproj` and validates that all 18
  package projects carry the same `<Version>`;
- skips publishing when that Core version is already on NuGet — bump the version to release, or use
  `force=true` on a manual run;
- packs and publishes in dependency order: pack the generator, log in with `NuGet/login@v1`, push it
  and wait for the nupkg to be downloadable; pack Core **without** `VeloxDevLocalCore` so its generator
  dependency comes from NuGet and stays in the nuspec, push it and wait; then pack the Extension,
  adapters, templates and CLI against the published Core/generator;
- asserts the Core nuspec keeps its `VeloxDev.Core.Generator` dependency and a representative adapter
  keeps its `VeloxDev.Core` dependency; pushes the rest with a fresh temporary key and
  `--skip-duplicate`; installs the CLI to verify it;
- if the version is `X1.X2.X3` with `X3 > 0`, unlists every older `X1.X2.<i>` for all 18 packages
  after the pushes, leaving only the highest patch of that line listed. A major/minor change
  leaves older lines listed. The policy must include the **Unlist or relist** scope.

The policy must name `publish.yml`, with owner/repo `Axvser/VeloxDev` and glob `VeloxDev.*`.
Auto or manual is a repository variable: set `AUTO_PUBLISH=true` to act on a push to `master` under
`Src/**`; leave it unset for manual-only. A manual run is always available:

```powershell
gh workflow run publish.yml --repo Axvser/VeloxDev -f force=false
```

`publish.yml` must exist on the default branch. The workflow uploads every nupkg as the
`VeloxDev.<version>` artifact; `$staging` is only for build-mode inspection.

## Verify

```powershell
$verify = Join-Path $staging "verify"
New-Item -ItemType Directory -Force $verify | Out-Null

foreach ($id in @(
    'VeloxDev.Core', 'VeloxDev.Core.Extension', 'VeloxDev.Core.Generator',
    'VeloxDev.WPF', 'VeloxDev.WPF.Templates', 'VeloxDev.Cli'
)) {
    Invoke-WebRequest "https://www.nuget.org/api/v2/package/$id/$version" `
        -OutFile (Join-Path $verify "$id.$version.nupkg") -UseBasicParsing
}

dotnet new install (Join-Path $verify "VeloxDev.WPF.Templates.$version.nupkg") `
    --debug:custom-hive (Join-Path $verify "hive")

dotnet tool install --tool-path (Join-Path $verify "tools") --add-source $verify `
    VeloxDev.Cli --version $version
$cli = if ($env:OS -eq 'Windows_NT') { 'veloxdev.exe' } else { 'veloxdev' }
& (Join-Path $verify "tools" $cli) --help
```

## Manual unlist — only on explicit request, outside the automatic patch cleanup

The publish workflow already unlists older patches of the same `X1.X2` automatically. This block is
for any **other** unlisting: do not run it unless the developer explicitly asks. Keep the newest N
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
| VeloxDev.Core.Generator | 11.0.0 | pushed / verified |
| VeloxDev.Core | 11.0.0 | pushed / verified |
| ... | ... | ... |
| VeloxDev.Cli | 11.0.0 | pushed / verified |
```

Report every package, the staging directory, whether the version bump is still uncommitted, and
the first failure if the run stopped. Patch releases unlist older same-`X1.X2` patches
automatically; any other unlisting requires an explicit request.
