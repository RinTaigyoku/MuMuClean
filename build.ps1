$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root 'src\Program.cs'
$manifest = Join-Path $root 'src\app.manifest'
$out = Join-Path $root 'dist\MuMu清爽助手.exe'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path -LiteralPath $csc)) { throw '未找到 .NET Framework C# 编译器。' }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $out) | Out-Null
& $csc /nologo /target:winexe /optimize+ /platform:anycpu /win32manifest:$manifest /out:$out /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll $src
if ($LASTEXITCODE -ne 0) { throw "编译失败，退出码 $LASTEXITCODE" }
Write-Host "已生成: $out"
Get-FileHash -Algorithm SHA256 -LiteralPath $out
