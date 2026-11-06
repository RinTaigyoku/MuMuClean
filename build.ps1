$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' | Where-Object { $_.Name -ne 'StartupListener.cs' } | ForEach-Object { $_.FullName })
$manifest = Join-Path $root 'src\app.manifest'
$icon = Join-Path $root 'assets\app.ico'
$out = Join-Path $root 'dist\MuMu清爽助手.exe'
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path -LiteralPath $csc)) { throw '未找到 .NET Framework C# 编译器。' }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $out) | Out-Null
$listener = Join-Path $root 'dist\MuMuStartup.exe'
$listenerManifest = Join-Path $root 'src\startup.manifest'
$listenerSource = Join-Path $root 'src\StartupListener.cs'
$policySource = Join-Path $root 'src\GuestAdPolicy.cs'
$uidSource = Join-Path $root 'src\GuestUidFilter.cs'
$uidBinary = Join-Path $root 'assets\UidFilter.x86_64'
$componentSource = Join-Path $root 'src\AdComponentControl.cs'
$commandSource = Join-Path $root 'src\NativeCommand.cs'
if (!(Test-Path -LiteralPath $uidBinary)) { throw '缺少 Android 12 身份过滤组件，请先构建 native\UidFilter.c。' }
& $csc /nologo /main:MuMuClean.StartupListener /target:winexe /optimize+ /platform:anycpu /win32manifest:$listenerManifest /out:$listener "/resource:$uidBinary,MuMuClean.UidFilter" /reference:System.dll /reference:System.Core.dll /reference:System.Web.Extensions.dll /reference:System.Windows.Forms.dll $listenerSource $policySource $uidSource $componentSource $commandSource
if ($LASTEXITCODE -ne 0) { throw '启动助手编译失败。' }
& $csc /nologo /main:MuMuClean.Program /target:winexe /optimize+ /platform:anycpu /win32manifest:$manifest /win32icon:$icon /out:$out "/resource:$uidBinary,MuMuClean.UidFilter" "/resource:$listener,MuMuClean.Startup.exe" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll $src
if ($LASTEXITCODE -ne 0) { throw "编译失败，退出码 $LASTEXITCODE" }
Write-Host "已生成: $out"
Get-FileHash -Algorithm SHA256 -LiteralPath $out
