<#
.SYNOPSIS
Compatibility wrapper for the WinForms workflow item-template check.

.DESCRIPTION
The check was generalised to all seven platforms in verify-workflow-item-templates-all.ps1; this file now forwards
to it for the WinForms platform, so existing invocations keep working and keep reporting the same thing.

It still proves: the seven winforms-v-* short names resolve; the seven generated files compile together against the
adapter; and template text still equals the mirror under Examples/Workflow/WinForms Trimmed/, after the mechanical
edits the mirror is allowed to carry (a namespace rooted in the demo, the demo-only HUD, and formatting).

.PARAMETER Strict
Makes template/mirror drift fatal.

.PARAMETER KeepProbe
Leaves the probe project on disk for inspection instead of deleting it.

.EXAMPLE
powershell -NoProfile -ExecutionPolicy Bypass -File Src/Verification/verify-workflow-item-templates.ps1
#>
[CmdletBinding()]
param(
    [switch] $Strict,
    [switch] $KeepProbe
)

$all = Join-Path $PSScriptRoot 'verify-workflow-item-templates-all.ps1'
$childArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $all, '-Platform', 'WinForms')
if ($Strict) { $childArgs += '-Strict' }
if ($KeepProbe) { $childArgs += '-KeepProbe' }

& powershell @childArgs
exit $LASTEXITCODE
