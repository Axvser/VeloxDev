param(
	[Parameter(Mandatory)][string]$Name,
	[Parameter(Mandatory)][string[]]$Files,
	[Parameter(Mandatory)][string]$Project,
	[Parameter(Mandatory)][string]$Filter,
	[string]$Extra = ''
)

# 严格 A/B：A = HEAD 版本的生产文件，B = 工作区版本；同一基准工程、同一过滤、各自重新构建。
$ErrorActionPreference = 'Stop'
$root = 'E:\VisualStudio\Projects\VeloxDev'
$out = Join-Path $env:TEMP "ab_$Name"
New-Item -ItemType Directory -Force $out | Out-Null
Set-Location $root

$backup = @{}
foreach ($f in $Files) { $backup[$f] = [IO.File]::ReadAllBytes((Join-Path $root $f)) }

function Run-Side([string]$side) {
	Push-Location (Join-Path $root $Project)
	try {
		dotnet build -c Debug -p:Optimize=true --no-incremental -v q -nologo *> "$out\build_$side.txt"
		if ($LASTEXITCODE -ne 0) { throw "BUILD FAILED ($side), see $out\build_$side.txt" }
		$args2 = @('run', '-c', 'Debug', '-p:Optimize=true', '--no-build', '--', '--filter', $Filter, '--job', 'short', '--inProcess')
		if ($Extra) { $args2 += $Extra.Split(' ') }
		dotnet @args2 *> "$out\run_$side.txt"
		if ($LASTEXITCODE -ne 0) { throw "RUN FAILED ($side), see $out\run_$side.txt" }
        # 0 个基准或 NA 都不算结果：前者是过滤没命中，后者是基准自己抛了异常。
        if (Select-String -Path "$out\run_$side.txt" -Pattern 'returned 0 benchmarks|\|\s+NA\s+\|' -Quiet) { throw "NO VALID RESULT ($side), see $out\run_$side.txt" }
	}
	finally { Pop-Location }
}

try {
	foreach ($f in $Files) {
		$git = $f -replace '\\','/'
		cmd /c "git cat-file -e HEAD:$git" 2>$null
		if ($LASTEXITCODE -ne 0) { throw "no HEAD version for $f" }
		# 经 cmd 重定向拿原始字节，避免 PowerShell 控制台编码改写中文注释。
		cmd /c "git show HEAD:$git > `"$(Join-Path $root $f)`""
		if ($LASTEXITCODE -ne 0) { throw "git show failed for $f" }
	}
	Run-Side 'A'
}
finally {
	foreach ($f in $Files) { [IO.File]::WriteAllBytes((Join-Path $root $f), $backup[$f]) }
}

Run-Side 'B'

foreach ($side in 'A', 'B') {
	"== $Name $side"
	Get-Content "$out\run_$side.txt" | Where-Object { $_ -match '^\| (Method|\w+)' -and $_ -notmatch '^\|-' } | Select-Object -Unique
}
