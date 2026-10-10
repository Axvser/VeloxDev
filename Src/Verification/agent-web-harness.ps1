<#
.SYNOPSIS
Drives a browser-hosted demo (Blazor) the way a human would — real mouse and keyboard through the
Chrome DevTools Protocol — so an agent can test interactive behaviour that no Win32 input can reach.

.DESCRIPTION
agent-ui-harness.ps1 injects input into a native window; that works for six of the seven platforms but not
for the Razor one. A Blazor Server demo is a page: SetCursorPos/SendInput drive the browser chrome, not the
document, and the link's Delete key is handled by the page's own @onkeydown, so it can never be exercised
that way. This script talks to Chrome/Edge over CDP instead (Input.dispatchMouseEvent /
Input.dispatchKeyEvent), which reaches the document.

It is also the better of the two for ASSERTING: `eval` runs JavaScript in the page and logs the result, so
"how many link paths are on the canvas" is a number in the log rather than a pixel you have to eyeball.

Coordinates are CSS pixels in the viewport — the same numbers `getBoundingClientRect()` gives you, so use
`eval` to ask the page where something is instead of reading it off a screenshot.

.PARAMETER Url
The page to open, e.g. http://localhost:49302

.PARAMETER Actions
An ordered list of steps, separated by ';;' (not ';': an eval payload is JavaScript, which is full of
semicolons):

  goto:<url>              navigate
  wait:<ms>               sleep
  shot:<name>             save <OutDir>\<name>.png  (captured even in headless)
  move:<x>,<y>            move the mouse there (fires mouseover/mouseenter)
  click:<x>,<y>           left click
  rclick:<x>,<y>          right click
  drag:<x1>,<y1>,<x2>,<y2> press, move in steps, release (creating a connection)
  wheel:<x>,<y>,<delta>   one *trusted* wheel notch at (x, y); 120 = up. Unlike a synthetic WheelEvent
                          dispatched from eval:, this one triggers the page's own default scrolling,
                          so it is the only way to measure how far the browser scrolls natively
  key:Delete              press and release a key (Delete | Escape | Enter)
  keydown:Shift           hold a modifier down (Shift | Control | Alt | Meta) — it is carried on the mouse
  keyup:Shift             events that follow, so a modified drag can be tested
  eval:<js>               run JavaScript and log its result — use it to locate elements and to assert
  cursor:<js>             move the pointer to a point the page computes (JS returning [x, y])
  waitfor:<selector>      wait until the selector matches something

.PARAMETER ServerExe / ServerArgs
Optional: something to start before browsing (e.g. -ServerExe dotnet -ServerArgs "run --project X"). It is
stopped at the end. Without it the script assumes the Url is already being served.

.EXAMPLE
powershell -NoProfile -ExecutionPolicy Bypass -File Src/Verification/agent-web-harness.ps1 `
  -ServerExe dotnet -ServerArgs @('run','--project','Examples/Workflow/Blazor Trimmed/Demo/Demo.csproj') `
  -Url http://localhost:49302 `
  -Actions "waitfor:.veloxdev-wf-surface; shot:start; eval:document.querySelectorAll('[data-veloxdev-link-curve]').length"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $Url,
    [Parameter(Mandatory = $true)][string] $Actions,
    [string] $OutDir = "$env:TEMP\veloxdev-web",
    [string] $ServerExe = '',
    [string[]] $ServerArgs = @(),
    [int] $Port = 9223,
    [int] $Width = 1280,
    [int] $Height = 800,
    [int] $BootSeconds = 18
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$log = Join-Path $OutDir 'web-harness.log'
Set-Content -Path $log -Value "web harness start $(Get-Date -Format o)" -Encoding utf8

$browser = @(
    "$env:ProgramFiles\Google\Chrome\Application\chrome.exe",
    "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe",
    "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
    "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $browser) { throw 'no Chrome or Edge found' }
Add-Content $log "browser $browser"

$server = $null
if ($ServerExe) {
    $server = if ($ServerArgs.Count -gt 0) { Start-Process -FilePath $ServerExe -ArgumentList $ServerArgs -PassThru -WindowStyle Hidden }
              else { Start-Process -FilePath $ServerExe -PassThru -WindowStyle Hidden }
    Add-Content $log "server pid $($server.Id), waiting $BootSeconds s"
    Start-Sleep -Seconds $BootSeconds
}

$profile = Join-Path $env:TEMP ("veloxdev-cdp-" + [Guid]::NewGuid().ToString('N'))
# --headless=new still composites and still runs the page's JS, so Blazor Server renders and CDP input lands.
$args = @(
    '--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check',
    "--remote-debugging-port=$Port", "--user-data-dir=$profile",
    "--window-size=$Width,$Height", 'about:blank'
)
$proc = Start-Process -FilePath $browser -ArgumentList $args -PassThru
Add-Content $log "browser pid $($proc.Id)"

# Wait for the debugging endpoint, then take the page target's WebSocket URL.
$target = $null
for ($i = 0; $i -lt 40 -and -not $target; $i++) {
    Start-Sleep -Milliseconds 500
    try {
        $target = (Invoke-RestMethod "http://127.0.0.1:$Port/json" -TimeoutSec 2) |
                  Where-Object { $_.type -eq 'page' } | Select-Object -First 1
    } catch { $target = $null }
}
if (-not $target) { Add-Content $log 'NO_CDP_TARGET'; Write-Output 'NO_CDP_TARGET'; exit 1 }

$ws = [System.Net.WebSockets.ClientWebSocket]::new()
$ws.ConnectAsync([Uri]$target.webSocketDebuggerUrl, [Threading.CancellationToken]::None).Wait()
Add-Content $log "cdp connected to $($target.webSocketDebuggerUrl)"

$script:cdpId = 0
function Send-Cdp {
    param([string] $Method, [hashtable] $Params = @{})
    $script:cdpId++
    $id = $script:cdpId
    $json = @{ id = $id; method = $Method; params = $Params } | ConvertTo-Json -Depth 30 -Compress
    $bytes = [Text.Encoding]::UTF8.GetBytes($json)
    $ws.SendAsync([ArraySegment[byte]]::new($bytes), [Net.WebSockets.WebSocketMessageType]::Text, $true,
                  [Threading.CancellationToken]::None).Wait()
    while ($true) {
        $buf = New-Object byte[] 4194304
        $ms = New-Object IO.MemoryStream
        do {
            $r = $ws.ReceiveAsync([ArraySegment[byte]]::new($buf), [Threading.CancellationToken]::None).Result
            $ms.Write($buf, 0, $r.Count)
        } while (-not $r.EndOfMessage)
        $msg = [Text.Encoding]::UTF8.GetString($ms.ToArray()) | ConvertFrom-Json
        if ($msg.id -eq $id) { return $msg }
    }
}

function Eval([string] $js) {
    $r = Send-Cdp 'Runtime.evaluate' @{ expression = $js; returnByValue = $true; awaitPromise = $true }
    if ($r.result.exceptionDetails) { return "EXCEPTION: $($r.result.exceptionDetails.text)" }
    return $r.result.result.value
}

function Shot([string] $name) {
    $r = Send-Cdp 'Page.captureScreenshot' @{ format = 'png' }
    $path = Join-Path $OutDir "$name.png"
    [IO.File]::WriteAllBytes($path, [Convert]::FromBase64String($r.result.data))
    Add-Content $log "shot $name -> $path"
    Write-Output "shot $path"
}

function Mouse([string] $type, [int] $x, [int] $y, [string] $button = 'none') {
    $p = @{ type = $type; x = $x; y = $y; button = $button; clickCount = 1; buttons = 0; modifiers = $script:heldModifiers }
    if ($button -eq 'left') { $p.buttons = 1 }
    if ($button -eq 'right') { $p.buttons = 2 }
    [void](Send-Cdp 'Input.dispatchMouseEvent' $p)
}

# Modifier bits as CDP defines them. Without a held modifier reaching the mouse events, a
# Shift-modified gesture is untestable here: the page sees a plain click.
$script:heldModifiers = 0

function ModifierBit([string] $name) {
    switch ($name) {
        'Alt' { 1 } 'Control' { 2 } 'Meta' { 4 } 'Shift' { 8 }
        default { throw "unsupported modifier: $name" }
    }
}

function SetModifier([string] $name, [bool] $down) {
    $bit = ModifierBit $name
    if ($down) { $script:heldModifiers = $script:heldModifiers -bor $bit }
    else { $script:heldModifiers = $script:heldModifiers -band (-bnot $bit) }

    $vk = switch ($name) { 'Alt' { 18 } 'Control' { 17 } 'Meta' { 91 } 'Shift' { 16 } }
    $type = if ($down) { 'rawKeyDown' } else { 'keyUp' }
    $base = @{ windowsVirtualKeyCode = $vk; nativeVirtualKeyCode = $vk; key = $name; code = $name; modifiers = $script:heldModifiers }
    [void](Send-Cdp 'Input.dispatchKeyEvent' ($base + @{ type = $type }))
}

function Key([string] $name) {
    $vk = switch ($name) { 'Delete' { 46 } 'Escape' { 27 } 'Enter' { 13 } default { throw "unsupported key: $name" } }
    $base = @{ windowsVirtualKeyCode = $vk; nativeVirtualKeyCode = $vk; key = $name; code = $name }
    [void](Send-Cdp 'Input.dispatchKeyEvent' ($base + @{ type = 'rawKeyDown' }))
    [void](Send-Cdp 'Input.dispatchKeyEvent' ($base + @{ type = 'keyUp' }))
}

[void](Send-Cdp 'Page.enable')
[void](Send-Cdp 'Runtime.enable')

# Navigate to the target page before running the actions: the browser starts on about:blank, and a Blazor
# Server page renders nothing at all until its circuit connects, so this also waits for a real document.
[void](Send-Cdp 'Page.navigate' @{ url = $Url })
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Milliseconds 500
    if ((Eval 'document.readyState') -eq 'complete') { break }
}
Add-Content $log "navigated to $Url (readyState=$((Eval 'document.readyState')))"

# Steps are separated by ';;' rather than ';' because an `eval:` payload is JavaScript, and JavaScript is
# full of single semicolons — splitting on one would tear a statement in half.
foreach ($action in ($Actions -split ';;')) {
    $action = $action.Trim()
    if ([string]::IsNullOrEmpty($action)) { continue }
    $verb, $arg = $action -split ':', 2

    switch ($verb) {
        'goto' { [void](Send-Cdp 'Page.navigate' @{ url = $arg }); Start-Sleep -Seconds 2; Add-Content $log "goto $arg" }
        'wait' { Start-Sleep -Milliseconds ([int]$arg) }
        'shot' { Shot $arg }
        'eval' {
            $value = Eval $arg
            Add-Content $log "eval [$arg] => $value"
            Write-Output "eval => $value"
        }
        'waitfor' {
            for ($i = 0; $i -lt 60; $i++) {
                if ((Eval "!!document.querySelector('$arg')") -eq $true) { break }
                Start-Sleep -Milliseconds 500
            }
            Add-Content $log "waitfor $arg done"
        }
        'move' {
            $p = $arg -split ','
            Mouse 'mouseMoved' ([int]$p[0]) ([int]$p[1]); Start-Sleep -Milliseconds 120
            Add-Content $log "move $arg"
        }
        'click' {
            $p = $arg -split ','
            Mouse 'mouseMoved' ([int]$p[0]) ([int]$p[1]); Start-Sleep -Milliseconds 80
            Mouse 'mousePressed' ([int]$p[0]) ([int]$p[1]) 'left'; Start-Sleep -Milliseconds 60
            Mouse 'mouseReleased' ([int]$p[0]) ([int]$p[1]) 'left'; Start-Sleep -Milliseconds 250
            Add-Content $log "click $arg"
        }
        'rclick' {
            $p = $arg -split ','
            Mouse 'mouseMoved' ([int]$p[0]) ([int]$p[1]); Start-Sleep -Milliseconds 80
            Mouse 'mousePressed' ([int]$p[0]) ([int]$p[1]) 'right'; Start-Sleep -Milliseconds 60
            Mouse 'mouseReleased' ([int]$p[0]) ([int]$p[1]) 'right'; Start-Sleep -Milliseconds 300
            Add-Content $log "rclick $arg"
        }
        'drag' {
            $p = $arg -split ','
            $x1 = [int]$p[0]; $y1 = [int]$p[1]; $x2 = [int]$p[2]; $y2 = [int]$p[3]
            Mouse 'mouseMoved' $x1 $y1; Start-Sleep -Milliseconds 120
            Mouse 'mousePressed' $x1 $y1 'left'; Start-Sleep -Milliseconds 180
            $steps = 12
            for ($i = 1; $i -le $steps; $i++) {
                Mouse 'mouseMoved' ($x1 + [int](($x2 - $x1) * $i / $steps)) ($y1 + [int](($y2 - $y1) * $i / $steps)) 'left'
                Start-Sleep -Milliseconds 40
            }
            Mouse 'mouseReleased' $x2 $y2 'left'; Start-Sleep -Milliseconds 350
            Add-Content $log "drag $arg"
        }
        'cursor' {
            # Moves the pointer to a point the PAGE computes: the payload is JavaScript returning [x, y].
            # This is how you land on a drawn path — ask the path where its own midpoint is, instead of
            # guessing through whatever transforms the canvas has applied.
            $v = Eval $arg
            if ($v) {
                $p = $v | ConvertFrom-Json
                Mouse 'mouseMoved' ([int]$p[0]) ([int]$p[1]); Start-Sleep -Milliseconds 200
            }
            Add-Content $log "cursor [$arg] => $v"
            Write-Output "cursor => $v"
        }
        # 可信滚轮：CDP 的 mouseWheel 是真输入事件，会触发页面的默认滚动 —— 合成的 WheelEvent
        # （eval: 里 dispatchEvent）不会，所以「原生滚了多少」只能靠这一条量。
        # 与桌面 harness 同号：正 = 远离用户（向上）。$script:heldModifiers 跟着走，Shift+滚轮因此可测。
        'wheel' {
            $p = $arg -split ','
            [void](Send-Cdp 'Input.dispatchMouseEvent' @{
                type      = 'mouseWheel'
                x         = [int]$p[0]
                y         = [int]$p[1]
                deltaX    = 0
                # CDP 用的是 DOM 符号（向下为正），桌面 harness 用的是操作系统符号（远离用户为正）——
                # 这里取反，让两边的 `wheel:` 是同一个意思：正 = 向上。
                deltaY    = -[int]$p[2]
                button    = 'none'
                buttons   = 0
                modifiers = $script:heldModifiers
            })
            Start-Sleep -Milliseconds 250
            Add-Content $log "wheel $arg"
        }
        'key' { Key $arg; Start-Sleep -Milliseconds 350; Add-Content $log "key $arg" }
        'keydown' { SetModifier $arg $true; Start-Sleep -Milliseconds 120; Add-Content $log "keydown $arg" }
        'keyup' { SetModifier $arg $false; Start-Sleep -Milliseconds 120; Add-Content $log "keyup $arg" }
        default { throw "unknown action: $action" }
    }
}

try { $ws.CloseAsync([Net.WebSockets.WebSocketCloseStatus]::NormalClosure, 'done', [Threading.CancellationToken]::None).Wait() } catch { }
$proc | Stop-Process -Force -ErrorAction SilentlyContinue
if ($server) { $server | Stop-Process -Force -ErrorAction SilentlyContinue }
Remove-Item -Recurse -Force $profile -ErrorAction SilentlyContinue
Add-Content $log 'web harness done'
Write-Output "log: $log"
