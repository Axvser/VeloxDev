---
name: veloxdev-release
description: Execute a VeloxDev NuGet release — bump the direct versions in the 16 package csproj files, pack and verify the Release artifacts, and, when asked to publish, push Core first, wait for the NuGet index, then push the Extension, seven adapters and seven template packs. Use ONLY when the developer explicitly asks to execute a release, publish, or release verification; never invoke it automatically, from another skill, or on an incidental mention. This skill is self-contained and unrelated to any other skill.
disable-model-invocation: true
argument-hint: "[version] [build|publish]"
---

# VeloxDev release

## Invocation contract

- **Run only on an explicit request.** The developer's current message must ask to *execute* this
  release — e.g. "release 10.2.0", "验证一下发布", "publish the packages". A passing mention of a
  release in another task is not an invocation. If the version or the mode (`build` / `publish`) is
  missing, ask before touching any file; do not guess.
- **This skill stands alone.** It does not load, chain into, or inherit rules from any other skill,
  and no other skill may call it. Its procedures are complete in this file and
  [references/package-plan.md](references/package-plan.md).
- **Publishing is irreversible.** NuGet package versions cannot be overwritten. Before the first
  `dotnet nuget push`, print the exact version and the 16 package ids and get an explicit "yes" if
  the request did not already name both. In build mode, never push.
- **Do not look for the old `Tools/` projects.** They were removed; this skill replaces them.

## Inputs

| Input | Default | Notes |
| --- | --- | --- |
| `version` | required | `x.y.z` or a SemVer prerelease; validated below |
| `mode` | required | `build` = pack + verify, push nothing; `publish` = push to NuGet |
| `NUGET_API_KEY` | required for publish | the environment variable; never echo its value |
| `pushSource` | `https://api.nuget.org/v3/index.json` | |
| `restoreSource` | `https://www.nuget.org/api/v2/` | v3 is redirected to a lagging mirror on some networks |
| `staging` | `%TEMP%/veloxdev-release-<version>` | where the nupkgs are collected |

The repository root is the directory containing `Src/Core/VeloxDev.Core/VeloxDev.Core.csproj`.
Run every PowerShell block from that root.

## Package plan

The authoritative 16-package list, the exact project paths, the version-bump script and the
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
3. Check every project path from the package plan exists, the tree is clean enough to bump 16
   csproj files, and—for publish—`$env:NUGET_API_KEY` is non-empty. Never print the key.
4. Create the staging directory and remove any old nupkgs from a previous attempt:
   ```powershell
   $staging = Join-Path $env:TEMP "veloxdev-release-$version"
   New-Item -ItemType Directory -Force $staging | Out-Null
   ```
5. `dotnet --version`; stop if `dotnet` is unavailable.

### 1. Bump the direct versions

Run the bump script in [references/package-plan.md](references/package-plan.md#bump). It rewrites
the literal `<Version>` in all 16 package projects and the literal
`Include="VeloxDev.Core" Version="…"` in the Extension and the seven adapters. The version is
written directly in the csproj; there is no `VeloxDevCoreVersion` property. Verify the rewritten
files before continuing; if a file has no `<Version>` or no rewritable Core reference, stop and
report the file.

After the bump, the working tree is intentionally dirty. Leave the version changes in place; commit
them only if the developer asks.

### 2. Pack and verify Core

```powershell
$core = "Src/Core/VeloxDev.Core/VeloxDev.Core.csproj"
dotnet clean $core -c Release
dotnet pack $core -c Release -o $staging -p:Version=$version -p:PackageVersion=$version
```

Check the produced package contains `lib/<tfm>/*.dll` entries (the package plan has the exact
PowerShell snippet). A Core package with no `lib/` assemblies is a failed release—stop.

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

Copy every produced nupkg into `$staging` (the package plan shows the expected filename). Verify
each id/version pair exists; report a table of `packed` packages. **Do not push in build mode.**

### 4. Publish mode

Only after the developer's explicit confirmation:

1. Push Core:
   ```powershell
   dotnet nuget push (Join-Path $staging "VeloxDev.Core.$version.nupkg") `
     --api-key $env:NUGET_API_KEY --source $pushSource --skip-duplicate
   ```
2. Wait for the index. Poll the v2 download endpoint until it returns 200; default poll 20s,
   timeout 30min. Do not pack the dependents before this succeeds.
3. Pack the Extension and adapters **without** `VeloxDevLocalCore`, using the v2 restore source:
   ```powershell
   dotnet restore $project -p:Configuration=Release --source $restoreSource
   dotnet build $project -c Release --no-restore -p:Version=$version -p:PackageVersion=$version
   ```
   The csproj already references `VeloxDev.Core` at `$version` from the bump; if it does not, stop
   instead of restoring a stale Core.
4. Push each dependent, then pack and push the seven templates. Use `--skip-duplicate` so a
   network retry cannot create a second state; a duplicate already at the same version is reported,
   not treated as a new push.
5. Never unlist old versions unless the developer explicitly asks. When asked, keep the newest N
   (default 2) and never unlist the version this run publishes.

### 5. Verify the published packages

Download the representative packages again from
`https://www.nuget.org/api/v2/package/<id>/<version>` (Core, Extension, WPF, WPF.Templates) and
install one template into a throw-away hive:

```powershell
dotnet new install <WPF.Templates nupkg> --debug:custom-hive $hive
```

A download that 404s or a template that will not install means the release is not complete; report
the failing package.

### 6. Report

Print one table: package id, version, status (`packed`, `pushed`, `verified`, `skipped`, `failed`)
and the staging path. State explicitly whether anything was pushed, whether the version bump is
still uncommitted, and any package that stopped the run.

## Stop conditions

- The developer's request is ambiguous, or the version/mode is missing.
- Any planned project is missing, a `<Version>` cannot be rewritten, or a `VeloxDev.Core` reference
  cannot be rewritten to `$version`.
- The Core package has no `lib/` assemblies.
- In publish mode: the API key is missing; the version already exists on NuGet and the developer
  did not explicitly ask to continue; the Core push fails; the Core does not appear within the
  timeout.
- A push fails. Stop the pipeline and report the package; do not continue with the rest.

## Do not

- Do not push in build mode, and do not treat "release" as a synonym for "publish".
- Do not run `dotnet nuget delete` / unlist versions unless explicitly asked.
- Do not add a `VeloxDevCoreVersion` property or a second version source: the csproj literal is the
  source, and this skill rewrites it.
- Do not invoke another skill, and do not let another task invoke this one.
