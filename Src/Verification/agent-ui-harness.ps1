<#
.SYNOPSIS
Drives a desktop demo the way a human would — real mouse and keyboard input, with screenshots — so an
agent can test interactive behaviour instead of reasoning about it.

.DESCRIPTION
Why this exists: "hover a link and press Delete" cannot be checked by building, and it cannot be checked by
reading either — the two defects it was written for (missing focus routes on five platforms) were invisible
to every check in the repo. This is the missing check.

The one thing that must not be got wrong is **DPI**. A process that is not per-monitor DPI aware gets
*virtualised* coordinates from GetWindowRect / SetCursorPos and a *scaled* CopyFromScreen, so the three
disagree and the cursor lands tens-to-hundreds of pixels away from where the screenshot says it is — input
then looks like it "does not arrive" when in fact it arrives somewhere harmless. The script calls
SetProcessDpiAwarenessContext at the top, before touching any coordinate, so all three agree in physical
pixels.

.PARAMETER Exe
Path to the demo executable.

.PARAMETER Actions
An ordered list of steps. Coordinates are **window-relative** (the screenshot's own pixels), so a step can
be read straight off a captured frame. Supported:

  shot:<name>                 save <OutDir>\<name>.png
  probe:<x>,<y>               log the pixel colour at that point (assert colour state in the log)
  move:<x>,<y>                move the pointer there (SetCursorPos)
  moveto:<x>,<y>              same, but through SendInput — the form that generates a real mouse-move message,
                              which is what node drags and WinUI hover need
  click:<x>,<y>               left click
  rclick:<x>,<y>              right click
  dblclick:<x>,<y>            double left click
  drag:<x1>,<y1>,<x2>,<y2>    press at the first point, move in steps, release at the second
  down:<x>,<y>                press and hold — pair with move:/shot: to see a mid-drag state, then up:
  up:<x>,<y>                  release the held button at that point
  key:Delete                  press and release a key (Delete | Escape | Enter | Back | Control | Shift)
  keydown:Control             hold a key down (pair it with keyup:, e.g. around wheel: for Ctrl+wheel zoom,
                              or around down:/up: for a Shift-modified drag)
  keyup:Control               release a key
  wheel:<x>,<y>,<delta>       one wheel notch at that point; 120 = up (zoom in), -120 = down
  type:<text>                 type ASCII text
  wait:<ms>

.PARAMETER OutDir
Where screenshots go. Every run also writes <OutDir>\harness.log with the window rect and each step.

.EXAMPLE
# pan the canvas by dragging empty space, then screenshot before and after
powershell -NoProfile -ExecutionPolicy Bypass -File Src/Verification/agent-ui-harness.ps1 `
  -Exe Examples/Workflow/WPF/Demo/bin/Debug/net9.0-windows/Demo.exe `
  -Actions "shot:before; drag:700,600,900,700; shot:after"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $Exe,
    # For framework-dependent demos that are a .dll rather than an .exe: pass -Exe dotnet -ExeArgs Demo.dll
    [string[]] $ExeArgs = @(),
    # One string, not string[]: PowerShell splits an array-typed argument on commas, which collides with the
    # comma-separated coordinates. Steps are separated by ';' instead.
    [Parameter(Mandatory = $true)][string] $Actions,
    [string] $OutDir = "$env:TEMP\veloxdev-ui",
    [int] $WaitSeconds = 14,
    [int] $X = 40,
    [int] $Y = 40,
    [int] $Width = 1280,
    [int] $Height = 800,
    [switch] $KeepRunning
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Ui {
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll", EntryPoint = "SendInput")] public static extern uint SendInput(uint n, INPUT[] inputs, int size);
  [DllImport("user32.dll", EntryPoint = "SendInput")] public static extern uint SendKeyInput(uint n, INPUTK[] inputs, int size);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [StructLayout(LayoutKind.Sequential)] public struct MI { public int dx, dy; public uint data, flags, time; public IntPtr extra; }
  [StructLayout(LayoutKind.Sequential)] public struct KI { public ushort vk, scan; public uint flags, time; public IntPtr extra; }
  [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public MI mi; }
  // INPUT's union is sized by its LARGEST member (MOUSEINPUT = 32 bytes on x64), so the whole struct is
  // 40 bytes whatever the payload. SendInput rejects any call whose cbSize is not exactly sizeof(INPUT) —
  // and it rejects it *silently* (returns 0), which is why a 32-byte keyboard struct makes every key press
  // vanish with no error anywhere. The pad restores the 40.
  [StructLayout(LayoutKind.Sequential)] public struct INPUTK { public uint type; public KI ki; public ulong pad; }

  // The *_FLAG suffix is load-bearing, not style: PowerShell resolves members case-insensitively, so a
  // constant named WHEEL or KEYUP would shadow the Wheel()/KeyUp() methods from the call site and the
  // call would fail with "MethodNotFound" while the method is plainly there.
  public const uint MOVE = 0x0001, ABSOLUTE = 0x8000, WHEEL_FLAG = 0x0800;
  public const uint LEFTDOWN = 0x0002, LEFTUP = 0x0004, RIGHTDOWN = 0x0008, RIGHTUP = 0x0010;
  public const uint KEYUP_FLAG = 0x0002;

  public static void Mouse(uint flags) {
    var a = new INPUT[1]; a[0].type = 0; a[0].mi.flags = flags;
    SendInput(1, a, Marshal.SizeOf(typeof(INPUT)));
  }
  // Absolute move through SendInput rather than SetCursorPos: SetCursorPos relocates the cursor without the
  // OS generating a mouse-move message, so frameworks that listen for their own pointer messages (WinUI,
  // and anything hooked natively) never see it. SendInput does generate them.
  public static uint MoveAbs(int screenX, int screenY) {
    var vx = GetSystemMetrics(0); var vy = GetSystemMetrics(1);
    var a = new INPUT[1]; a[0].type = 0;
    a[0].mi.flags = MOVE | ABSOLUTE;
    a[0].mi.dx = (int)Math.Round(screenX * 65535.0 / Math.Max(1, vx - 1));
    a[0].mi.dy = (int)Math.Round(screenY * 65535.0 / Math.Max(1, vy - 1));
    return SendInput(1, a, Marshal.SizeOf(typeof(INPUT)));
  }
  [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
  // One wheel notch carries the distance in data (not in dx/dy), and the sign is the direction:
  // positive is away from the user, which every framework reads as "scroll up / zoom in".
  public static uint Wheel(int delta) {
    var a = new INPUT[1]; a[0].type = 0; a[0].mi.flags = WHEEL_FLAG; a[0].mi.data = unchecked((uint)delta);
    return SendInput(1, a, Marshal.SizeOf(typeof(INPUT)));
  }
  public static uint KeyDown(ushort vk) {
    var d = new INPUTK[1]; d[0].type = 1; d[0].ki.vk = vk;
    return SendKeyInput(1, d, Marshal.SizeOf(typeof(INPUTK)));
  }
  public static uint KeyUp(ushort vk) {
    var u = new INPUTK[1]; u[0].type = 1; u[0].ki.vk = vk; u[0].ki.flags = KEYUP_FLAG;
    return SendKeyInput(1, u, Marshal.SizeOf(typeof(INPUTK)));
  }
  // Absolute move: SendInput's absolute form is normalised over the virtual screen, which is what makes it
  // land identically on multi-monitor / scaled setups where a bare SetCursorPos is ambiguous.
  // Returns how many events the OS actually accepted (2 = a full press+release). Anything else means the
  // input never happened — surface it instead of assuming.
  public static uint Key(ushort vk) {
    // Add-Type compiles with the legacy CodeDom compiler (C# 5), so no expression-bodied members here.
    var n = KeyDown(vk);
    n += KeyUp(vk);
    return n;
  }
}
"@

# Must run before any coordinate is read or written.
[void][Ui]::SetProcessDpiAwarenessContext([IntPtr](-4))   # PER_MONITOR_AWARE_V2

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$log = Join-Path $OutDir 'harness.log'
Set-Content -Path $log -Value "harness start $(Get-Date -Format o)" -Encoding utf8

# A leftover instance of the same exe locks its own bin\ DLLs, so the next build silently keeps the old
# binary and the run then tests yesterday's code. Kill only processes running exactly this exe — never when
# the host is a shared launcher like dotnet, where that match would reach unrelated processes.
$exeFull = $null
try { $exeFull = (Resolve-Path -LiteralPath $Exe -ErrorAction Stop).Path } catch { $exeFull = $null }
if ($exeFull -and [System.IO.Path]::GetFileNameWithoutExtension($exeFull) -ne 'dotnet') {
    Get-Process -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -eq $exeFull } |
        ForEach-Object { $_.Kill(); Add-Content $log "killed a leftover instance (pid $($_.Id))" }
    Start-Sleep -Milliseconds 400
}

$launchedAt = Get-Date
$proc = if ($ExeArgs.Count -gt 0) { Start-Process -FilePath $Exe -ArgumentList $ExeArgs -PassThru }
        else { Start-Process -FilePath $Exe -PassThru }
Start-Sleep -Seconds $WaitSeconds

# Do not assume the window belongs to the process we started: `dotnet Foo.dll` may hand it to another
# process, and the launcher itself can exit. Prefer our own process, then fall back to "a window that
# appeared after we launched".
$h = [IntPtr]::Zero
$proc.Refresh()
if (-not $proc.HasExited) { $h = $proc.MainWindowHandle }
for ($i = 0; $i -lt 40 -and ($h -eq [IntPtr]::Zero -or $h -eq $null); $i++) {
    $h = Get-Process -ErrorAction SilentlyContinue |
        Where-Object { try { $_.MainWindowHandle -ne 0 -and $_.StartTime -ge $launchedAt } catch { $false } } |
        Sort-Object StartTime -Descending |
        Select-Object -First 1 -ExpandProperty MainWindowHandle
    if ($null -eq $h) { $h = [IntPtr]::Zero }
    if ($h -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 500 }
}

if ($null -eq $h -or $h -eq [IntPtr]::Zero) { Add-Content $log 'NO_WINDOW'; Write-Output 'NO_WINDOW'; exit 1 }
Add-Content $log "window handle $h"

[void][Ui]::SetWindowPos($h, [IntPtr]::Zero, $X, $Y, $Width, $Height, 0x0040)   # SWP_SHOWWINDOW
[void][Ui]::SetForegroundWindow($h)
Start-Sleep -Milliseconds 900

$rect = New-Object Ui+RECT
[void][Ui]::GetWindowRect($h, [ref]$rect)
$ox = $rect.L; $oy = $rect.T
Add-Content $log "window rect $($rect.L),$($rect.T) - $($rect.R),$($rect.B)  (physical px, dpi-aware)"

function Shot([string]$name) {
    $r = New-Object Ui+RECT
    [void][Ui]::GetWindowRect($h, [ref]$r)
    $w = $r.R - $r.L
    $ht = $r.B - $r.T
    Add-Content $log "shot $name rect=$($r.L),$($r.T)-$($r.R),$($r.B) size=${w}x${ht}"
    if ($w -le 0 -or $ht -le 0) {
        Add-Content $log "shot $name SKIPPED (window rect is empty)"
        return
    }

    $bmp = New-Object System.Drawing.Bitmap($w, $ht)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)
    $g.Dispose()
    $path = Join-Path $OutDir "$name.png"
    $fs = [System.IO.File]::Create($path)
    try { $bmp.Save($fs, [System.Drawing.Imaging.ImageFormat]::Png) } finally { $fs.Close(); $bmp.Dispose() }
    Add-Content $log "shot $name -> $path"
    Write-Output "shot $path"
}

function ToClient([int]$cx, [int]$cy) { [void][Ui]::SetCursorPos($ox + $cx, $oy + $cy); Start-Sleep -Milliseconds 70 }

function ToVk([string]$name) {
    switch ($name) {
        'Delete' { 0x2E } 'Escape' { 0x1B } 'Enter' { 0x0D } 'Back' { 0x08 } 'Control' { 0x11 } 'Shift' { 0x10 }
        default { throw "unsupported key: $name" }
    }
}

foreach ($action in ($Actions -split ';')) {
    $action = $action.Trim()
    if ([string]::IsNullOrEmpty($action)) { continue }
    $verb, $arg = $action -split ':', 2
    switch ($verb) {
        'shot' { Shot $arg }
        'probe' {
            # Reads one pixel off the live window and logs it. Lets a script assert a colour state
            # (e.g. "the hovered link is no longer the resting colour") instead of judging by eye.
            $p = $arg -split ','
            $px = $ox + [int]$p[0]; $py = $oy + [int]$p[1]
            $r = New-Object Ui+RECT
            [void][Ui]::GetWindowRect($h, [ref]$r)
            $bmp = New-Object System.Drawing.Bitmap(1, 1)
            $g = [System.Drawing.Graphics]::FromImage($bmp)
            $g.CopyFromScreen($px, $py, 0, 0, (New-Object System.Drawing.Size(1, 1)))
            $g.Dispose()
            $c = $bmp.GetPixel(0, 0)
            $bmp.Dispose()
            Add-Content $log ("probe {0} = #{1:X2}{2:X2}{3:X2}" -f $arg, $c.R, $c.G, $c.B)
        }
        'wait' { Start-Sleep -Milliseconds ([int]$arg) }
        'move' {
            $p = $arg -split ','
            ToClient ([int]$p[0]) ([int]$p[1])
            Add-Content $log "move $arg"
        }
        'moveto' {
            # Same coordinates as move:, but through SendInput — the only form the frameworks that read their
            # own pointer messages actually see (node drags, hover on WinUI). Optional third field is the
            # settle time in ms (default 90); drop it to ~10 to emulate a real mouse and see whether the app
            # keeps up with the event rate.
            $p = $arg -split ','
            [void][Ui]::MoveAbs($ox + [int]$p[0], $oy + [int]$p[1])
            $settle = if ($p.Count -ge 3) { [int]$p[2] } else { 90 }
            Start-Sleep -Milliseconds $settle
            Add-Content $log "moveto $arg"
        }
        'click' {
            $p = $arg -split ','
            ToClient ([int]$p[0]) ([int]$p[1])
            [Ui]::Mouse([Ui]::LEFTDOWN); Start-Sleep -Milliseconds 60
            [Ui]::Mouse([Ui]::LEFTUP); Start-Sleep -Milliseconds 250
            Add-Content $log "click $arg"
        }
        'rclick' {
            $p = $arg -split ','
            ToClient ([int]$p[0]) ([int]$p[1])
            [Ui]::Mouse([Ui]::RIGHTDOWN); Start-Sleep -Milliseconds 60
            [Ui]::Mouse([Ui]::RIGHTUP); Start-Sleep -Milliseconds 400
            Add-Content $log "rclick $arg"
        }
        'dblclick' {
            $p = $arg -split ','
            ToClient ([int]$p[0]) ([int]$p[1])
            [Ui]::Mouse([Ui]::LEFTDOWN); [Ui]::Mouse([Ui]::LEFTUP); Start-Sleep -Milliseconds 60
            [Ui]::Mouse([Ui]::LEFTDOWN); [Ui]::Mouse([Ui]::LEFTUP); Start-Sleep -Milliseconds 250
            Add-Content $log "dblclick $arg"
        }
        'down' {
            $p = $arg -split ','
            ToClient ([int]$p[0]) ([int]$p[1])
            [Ui]::Mouse([Ui]::LEFTDOWN); Start-Sleep -Milliseconds 200
            Add-Content $log "down $arg"
        }
        'up' {
            $p = $arg -split ','
            ToClient ([int]$p[0]) ([int]$p[1])
            [Ui]::Mouse([Ui]::LEFTUP); Start-Sleep -Milliseconds 350
            Add-Content $log "up $arg"
        }
        'drag' {
            $p = $arg -split ','
            ToClient ([int]$p[0]) ([int]$p[1])
            [Ui]::Mouse([Ui]::LEFTDOWN); Start-Sleep -Milliseconds 200
            $steps = 14
            for ($i = 1; $i -le $steps; $i++) {
                $nx = [int]$p[0] + [int](($p[2] - $p[0]) * $i / $steps)
                $ny = [int]$p[1] + [int](($p[3] - $p[1]) * $i / $steps)
                ToClient $nx $ny
            }
            [Ui]::Mouse([Ui]::LEFTUP); Start-Sleep -Milliseconds 350
            Add-Content $log "drag $arg"
        }
        'key' {
            $accepted = [Ui]::Key([uint16](ToVk $arg))
            if ($accepted -ne 2) { Add-Content $log "key $arg REJECTED (SendInput accepted $accepted/2)" }
            else { Add-Content $log "key $arg" }
            Start-Sleep -Milliseconds 350
        }
        'keydown' {
            $accepted = [Ui]::KeyDown([uint16](ToVk $arg))
            if ($accepted -ne 1) { Add-Content $log "keydown $arg REJECTED (SendInput accepted $accepted/1)" }
            else { Add-Content $log "keydown $arg" }
            Start-Sleep -Milliseconds 120
        }
        'keyup' {
            $accepted = [Ui]::KeyUp([uint16](ToVk $arg))
            if ($accepted -ne 1) { Add-Content $log "keyup $arg REJECTED (SendInput accepted $accepted/1)" }
            else { Add-Content $log "keyup $arg" }
            Start-Sleep -Milliseconds 120
        }
        'wheel' {
            $p = $arg -split ','
            ToClient ([int]$p[0]) ([int]$p[1])
            $accepted = [Ui]::Wheel([int]$p[2])
            if ($accepted -ne 1) { Add-Content $log "wheel $arg REJECTED (SendInput accepted $accepted/1)" }
            else { Add-Content $log "wheel $arg" }
            Start-Sleep -Milliseconds 220
        }
        'type' {
            foreach ($ch in $arg.ToCharArray()) {
                if ($ch -ge 'a' -and $ch -le 'z') { [void][Ui]::Key([uint16](0x41 + ([int][char]$ch - [int][char]'a'))) }
                elseif ($ch -ge 'A' -and $ch -le 'Z') { [void][Ui]::Key([uint16](0x41 + ([int][char]$ch - [int][char]'A'))) }
                elseif ($ch -ge '0' -and $ch -le '9') { [void][Ui]::Key([uint16](0x30 + ([int][char]$ch - [int][char]'0'))) }
                elseif ($ch -eq ' ') { [void][Ui]::Key(0x20) }
            }
            Start-Sleep -Milliseconds 150
            Add-Content $log "type $arg"
        }
        default { throw "unknown action: $action" }
    }
}

if (-not $KeepRunning) {
    $proc.Refresh()
    if (-not $proc.HasExited) { $proc.CloseMainWindow() | Out-Null; Start-Sleep -Milliseconds 600 }
    if (-not $proc.HasExited) { $proc.Kill() }
}
Add-Content $log 'harness done'
Write-Output "log: $log"
