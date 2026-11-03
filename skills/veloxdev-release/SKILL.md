---
name: veloxdev-release
description: Execute a VeloxDev NuGet release — bump the direct versions in the 18 package csproj files (including the generator and the CLI tool), pack and verify the Release artifacts locally, and, when asked to publish, run the publish.yml GitHub Actions workflow that pushes the generator, Core, Extension, seven adapters, seven template packs and the CLI through NuGet Trusted Publishing. Use ONLY when the developer explicitly asks to execute a release, publish, or release verification; never invoke it automatically, from another skill, or on an incidental mention. This skill is self-contained and unrelated to any other skill.
disable-model-invocation: true
argument-hint: "[version] [build|publish]"
---

# VeloxDev release

## Invocation contract

- **Run only on an explicit request.** The developer's current message must ask to *execute* this
  release — e.g. "release 11.0.0", "验证一下发布", "publish the packages". A passing mention of a
  release in another task is not an invocation. If the version or the mode (`build` / `publish`) is
  missing, ask before touching any file; do not guess.
- **This skill stands alone.** It does not load, chain into, or inherit rules from any other skill,
  and no other skill may call it. Its procedures are complete in this file and
  [references/package-plan.md](references/package-plan.md).
- **Publishing is irreversible.** NuGet package versions cannot be overwritten. Before triggering
  the publish workflow, print the exact version and the 18 package ids and get an explicit "yes" if
  the request did not already name both. In build mode, never push. The local session never pushes;
  the GitHub Actions workflow is the only publisher.
- **Do not look for the old `Tools/` projects.** They were removed; this skill replaces them.

## Inputs

| Input | Default | Notes |
| --- | --- | --- |
| `version` | required | `x.y.z` or a SemVer prerelease; validated below |
| `mode` | required | `build` = pack + verify, push nothing; `publish` = push to NuGet |
| `NUGET_API_KEY` | not used by publishing | Trusted Publishing uses GitHub OIDC for push and patch unlisting. A classic key is only needed for an explicit manual unlist outside the workflow. |
| `pushSource` | `https://api.nuget.org/v3/index.json` | |
| `restoreSource` | `https://www.nuget.org/api/v2/` | v3 is redirected to a lagging mirror on some networks |
| `staging` | `%TEMP%/veloxdev-release-<version>` | where the nupkgs are collected |

The repository root is the directory containing `Src/Core/VeloxDev.Core/VeloxDev.Core.csproj`.
Run every PowerShell block from that root.

## Package plan

The authoritative 18-package list, the exact project paths, the version-bump script and the
verification commands are in [references/package-plan.md](references/package-plan.md). Read it
before the first command; do not infer packages from directory names.

## Procedure

### 0. Gate and preflight

1. Re-read the developer's request. If it is not an explicit execution request, stop and say so.
   If `mode` is `publish`, confirm the version and the package ids are what the developer asked for.
2. Validate the version:
   ```powershell
   if ($version -notmatch '^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$') { throw "invalid version '$version'" }
   ```
3. Check every project path from the package plan exists and the tree is clean enough to bump 18
   csproj files. For publish, confirm the NuGet Trusted Publishing policy names `publish.yml`; no
   API key is read or printed.
4. Create the staging directory and remove any old nupkgs from a previous attempt:
   ```powershell
   $staging = Join-Path $env:TEMP "veloxdev-release-$version"
   New-Item -ItemType Directory -Force $staging | Out-Null
   ```
5. `dotnet --version`; stop if `dotnet` is unavailable.

### 1. Bump the direct versions

Run the bump script in [references/package-plan.md](references/package-plan.md#bump). It rewrites
the literal `<Version>` in all 18 package projects — including the generator and the CLI tool — and
the literal `Include="VeloxDev.Core" Version="…"` in the Extension and the seven adapters. The version is
written directly in the csproj; there is no `VeloxDevCoreVersion` property. Verify the rewritten
files before continuing; if a file has no `<Version>` or no rewritable Core reference, stop and
report the file.

After the bump, the working tree is intentionally dirty. The publish workflow reads the version from
the **default branch**, so a version bump must be committed and pushed to `master` before a
push-triggered or manual workflow run will use it. Do not commit without the developer's request; if
publishing is the task, the version bump is what the commit carries.

### 2. Pack the generator and Core

`GeneratePackageOnBuild=true` (set on both projects) removes `Build` from `GenerateNuspecDependsOn`,
so a bare `dotnet pack` restores and then packs a DLL that was never built (`NU5026`; the generator
additionally reports `NU5019` for its analyzer payload). Build first, then pack the built output:

```powershell
dotnet build "Src/Generators/VeloxDev.Core.Generator/VeloxDev.Core.Generator.csproj" -c Release `
    -p:Version=$version -p:PackageVersion=$version -p:GeneratePackageOnBuild=false
dotnet pack "Src/Generators/VeloxDev.Core.Generator/VeloxDev.Core.Generator.csproj" -c Release -o $staging --no-build `
    -p:Version=$version -p:PackageVersion=$version

$core = "Src/Core/VeloxDev.Core/VeloxDev.Core.csproj"
dotnet clean $core -c Release
dotnet build $core -c Release -p:Version=$version -p:PackageVersion=$version `
    -p:VeloxDevLocalCore=true -p:GeneratePackageOnBuild=false
dotnet pack $core -c Release -o $staging --no-build -p:Version=$version -p:PackageVersion=$version -p:VeloxDevLocalCore=true
```

Check the produced Core package contains `lib/<tfm>/*.dll` entries (the package plan has the exact
PowerShell snippet). A Core package with no `lib/` assemblies is a failed release—stop. The
`VeloxDevLocalCore=true` flag makes Core's generator package reference resolve to the working tree.

### 3. Build mode: pack and verify the rest

For the Extension and the seven adapters, build in Release against the **working tree's Core**
without requiring the new Core on NuGet:

```powershell
dotnet restore $project -p:Configuration=Release -p:VeloxDevLocalCore=true --source $restoreSource
dotnet build $project -c Release --no-restore -p:Version=$version -p:PackageVersion=$version -p:VeloxDevLocalCore=true
```

`Src/Directory.Build.targets` replaces the `VeloxDev.Core` package reference with the
`VeloxDev.Core.csproj` project reference when `VeloxDevLocalCore=true`; this is why the csproj files
stay at their direct versions. Then pack the seven template projects:

```powershell
dotnet pack $template -c Release -o $staging -p:Version=$version -p:PackageVersion=$version
```

Pack the CLI as a tool, against the working-tree Core:

```powershell
dotnet pack "Src/Cli/VeloxDev.Cli/VeloxDev.Cli.csproj" -c Release -o $staging `
    -p:Version=$version -p:PackageVersion=$version -p:VeloxDevLocalCore=true
```

Copy every produced nupkg into `$staging` (the package plan shows the expected filename). Verify
each id/version pair exists; report a table of `packed` packages. **Do not push in build mode.**

### 4. Publish mode — the GitHub Actions workflow

**No package is pushed from the local session.** Publishing is executed by
[`.github/workflows/publish.yml`](../../.github/workflows/publish.yml) through NuGet Trusted
Publishing (GitHub OIDC), which covers all 18 packages.

1. The version is whatever is written in `Src/Core/VeloxDev.Core/VeloxDev.Core.csproj`; the workflow
   reads it and refuses to run if any of the 18 package projects carries a different `<Version>`.
2. Every package is checked against NuGet for that version; only the missing ones are packed and
   pushed, so a run that stopped halfway can be retried as-is without repacking what already landed.
   When all 18 are already present the push-triggered run publishes nothing — bump the version and
   commit/push the bump to release. A manual run with `force=true` treats every package as missing
   and re-runs the whole pipeline (duplicates are skipped at push time).
3. The workflow is **sequential against NuGet**, not a local build:
   pack the generator → log in with `NuGet/login@v1` → push it and wait until the nupkg is downloadable;
   pack Core **without** `VeloxDevLocalCore` (so its generator dependency resolves from NuGet and lands
   in the nuspec) → push Core and wait again; then pack the Extension, adapters, templates and CLI
   against the published Core/generator, and push them with a fresh temporary key. Each of those steps
   is skipped when its packages are already on NuGet; the waits only run for what was just pushed.
   The workflow asserts that the Core nuspec keeps its generator dependency and that a representative
   adapter keeps its Core dependency, then installs the CLI tool to verify it. It must exist on the
   default branch.
4. **Patch cleanup is automatic.** If the version is `X1.X2.X3` with `X3 > 0`, after publishing
   the workflow unlists every older `X1.X2.<i>` (`i < X3`) for all 18 packages, so only the highest
   patch of that major/minor line stays listed. A major/minor change leaves older lines alone.
   The policy must include the **Unlist or relist** scope. Any other unlisting still requires an
   explicit request and the manual block in the package plan.

**Auto or manual.** The push trigger is only acted on when the repository variable `AUTO_PUBLISH`
is `true` (Settings → Secrets and variables → Actions → Variables). Unset or `false` makes the
workflow manual-only: a push starts a skipped run, and nothing is built. Manual runs are always
available:

```powershell
gh workflow run publish.yml --repo Axvser/VeloxDev -f force=false
```

For auto release: set `AUTO_PUBLISH=true`, bump `<Version>`, commit and push to `master`.
For manual release: leave the variable unset, bump and push, then run the workflow from the Actions
UI or `gh`.

### 4b. The Trusted Publishing policy

The NuGet.org policy must match the workflow exactly:

| Field | Value |
| --- | --- |
| Package Owner | `Axvser` |
| CI/CD Provider | GitHub Actions |
| Repository Owner | `Axvser` (the GitHub account, never an email) |
| Repository | `VeloxDev` |
| Workflow File | `publish.yml` (file name only) |
| Environment | blank |
| Package scope | `VeloxDev.*` |
| Scopes | Push new packages and package versions + Unlist or relist package versions |

One policy covers every `VeloxDev.*` package because the workflow is the single publisher for all
18. The policy is temporary for 7 days until the first successful OIDC login; run the workflow
within that window. A new workflow file requires a new policy.

### 5. Verify the published packages

Download the representative packages again from
`https://www.nuget.org/api/v2/package/<id>/<version>` (Core, Extension, Generator, WPF,
WPF.Templates, CLI), install one template into a throw-away hive, and install the CLI tool:

```powershell
dotnet new install <WPF.Templates nupkg> --debug:custom-hive $hive
dotnet tool install --tool-path $tools --add-source $verify VeloxDev.Cli --version $version
$cli = if ($env:OS -eq 'Windows_NT') { 'veloxdev.exe' } else { 'veloxdev' }
& (Join-Path $tools $cli) --help
```

A download that 404s, a template that will not install, or a tool whose `--help` fails means the
release is not complete; report the failing package.

### 6. Report

Print one table for all 18 packages: package id, version, status (`packed`, `pushed`, `verified`,
`skipped`, `failed`) and the staging path. State explicitly whether anything was pushed, which
older patches were unlisted, whether the version bump is still uncommitted, and any package that
stopped the run.

## Stop conditions

- The developer's request is ambiguous, or the version/mode is missing.
- Any planned project is missing, a `<Version>` cannot be rewritten, or a `VeloxDev.Core` reference
  cannot be rewritten to `$version`.
- The Core package has no `lib/` assemblies.
- In publish mode: the Trusted Publishing policy is missing or names a different workflow file; the
  workflow is not on the default branch; the workflow run fails; a package does not appear within
  its index timeout.
- A workflow step fails. Stop and report the failing package/step; do not retry the whole release
  without fixing the cause.

## Do not

- Do not push in build mode, and do not treat "release" as a synonym for "publish".
- Do not push any VeloxDev package from the local session; use `.github/workflows/publish.yml`.
- Do not run a manual `dotnet nuget delete` / unlist unless explicitly asked; the workflow's
  same-`X1.X2` patch cleanup is automatic.
- Do not add a `VeloxDevCoreVersion` property or a second version source: the csproj literal is the
  source, and this skill rewrites it.
- Do not invoke another skill, and do not let another task invoke this one.
