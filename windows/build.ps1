# Build script for VoxIA Windows application
$cscPath = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path $cscPath)) {
    Write-Host "[ERRO] Compilador C# nao encontrado em $cscPath" -ForegroundColor Red
    exit 1
}

Write-Host "Compilando VoxIA.exe usando $cscPath..." -ForegroundColor Cyan

$scriptDir = if ($PSScriptRoot) { $PSScriptRoot } else { (Get-Location).Path }
$sources = Get-ChildItem -Path (Join-Path $scriptDir "src") -Recurse -Filter "*.cs" | Select-Object -ExpandProperty FullName
$references = "/lib:`"C:\Program Files\Reference Assemblies\Microsoft\Framework\v3.0`" /r:System.dll,System.Drawing.dll,System.Windows.Forms.dll,System.Web.Extensions.dll,System.Net.Http.dll,UIAutomationClient.dll,UIAutomationTypes.dll"

$iconPath = Join-Path $scriptDir "app.ico"
$iconArg = ""
if (Test-Path $iconPath) {
    $iconArg = "/win32icon:`"$iconPath`""
}

$targetOut = Join-Path $scriptDir "VoxIA.exe"
$cmd = "& '$cscPath' $references $iconArg /target:winexe /out:`"$targetOut`" $sources"
Invoke-Expression $cmd

if ($LASTEXITCODE -ne 0) {
    # If in use by active process, compile to VoxIA_Update.exe
    $targetOut = Join-Path $scriptDir "VoxIA_Update.exe"
    $cmd = "& '$cscPath' $references $iconArg /target:winexe /out:`"$targetOut`" $sources"
    Invoke-Expression $cmd
}

if ($LASTEXITCODE -eq 0) {
    Write-Host "[SUCESSO] $targetOut compilado com sucesso com icone integrado!" -ForegroundColor Green
} else {
    Write-Host "[ERRO] Falha na compilacao do VoxIA" -ForegroundColor Red
}
