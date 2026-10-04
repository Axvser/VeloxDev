<#
.SYNOPSIS
Compatibility wrapper for the Jalium workflow item-template check.

.DESCRIPTION
The check was generalised to all seven platforms in verify-workflow-item-templates-all.ps1; this file now forwards
to it for the Jalium platform, so existing invocations keep working and keep reporting the same thing.

It still proves: the seven jalium-v-* short names resolve; the seven generated files compile together against the
adapter; and template text still equals the mirror under Examples/Workflow/Jalium Trimmed/, after the mechanical
edits the mirror is allowed to carry (the demo-rooted namespace, the colour spelling, and formatting).

It also fails on an un-substituted template placeholder (a symbol the template references but does not declare),
which the old colour-normalising comparison could not see.

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

$all = Join-Path $PSScriptRoot 'verify-workflow-item-templates-all.ps1'
$childArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $all, '-Platform', 'Jalium')
if ($Strict) { $childArgs += '-Strict' }
if ($KeepProbe) { $childArgs += '-KeepProbe' }

& powershell @childArgs
exit $LASTEXITCODE
