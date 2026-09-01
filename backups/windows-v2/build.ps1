# Build script for VoxIA Windows V2 (com Sincronização Supabase)
$cscPath = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path $cscPath)) {
    Write-Host "[ERRO] Compilador C# nao encontrado em $cscPath" -ForegroundColor Red
    exit 1
}

Write-Host "Compilando VoxIA-v2.exe usando $cscPath..." -ForegroundColor Cyan

$sources = Get-ChildItem -Path "." -Recurse -Filter "*.cs" | Select-Object -ExpandProperty FullName
$references = "/lib:`"C:\Program Files\Reference Assemblies\Microsoft\Framework\v3.0`" /r:System.dll,System.Drawing.dll,System.Windows.Forms.dll,System.Web.Extensions.dll,System.Net.Http.dll,UIAutomationClient.dll,UIAutomationTypes.dll"

$iconArg = ""
if (Test-Path "..\app.ico") {
    $iconArg = "/win32icon:..\app.ico"
} elseif (Test-Path "app.ico") {
    $iconArg = "/win32icon:app.ico"
}

$targetOut = "VoxIA-v2.exe"
$cmd = "& '$cscPath' $references $iconArg /target:winexe /out:$targetOut $sources"
Invoke-Expression $cmd

if ($LASTEXITCODE -eq 0) {
    Write-Host "[SUCESSO] $targetOut compilado com sucesso!" -ForegroundColor Green
} else {
    Write-Host "[ERRO] Falha na compilacao do VoxIA-v2" -ForegroundColor Red
}
