# Build script for VoxIA Windows application
$cscPath = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path $cscPath)) {
    Write-Host "[ERRO] Compilador C# nao encontrado em $cscPath" -ForegroundColor Red
    exit 1
}

Write-Host "Compilando VoxIA.exe usando $cscPath..." -ForegroundColor Cyan

$sources = Get-ChildItem -Path "src" -Recurse -Filter "*.cs" | Select-Object -ExpandProperty FullName
$references = "/lib:`"C:\Program Files\Reference Assemblies\Microsoft\Framework\v3.0`" /r:System.dll,System.Drawing.dll,System.Windows.Forms.dll,System.Web.Extensions.dll,System.Net.Http.dll,UIAutomationClient.dll,UIAutomationTypes.dll"

$iconArg = ""
if (Test-Path "app.ico") {
    $iconArg = "/win32icon:app.ico"
}

$cmd = "& '$cscPath' $references $iconArg /target:winexe /out:VoxIA.exe $sources"
Invoke-Expression $cmd

if ($LASTEXITCODE -eq 0) {
    Write-Host "[SUCESSO] VoxIA.exe compilado com sucesso com icone integrado!" -ForegroundColor Green
} else {
    Write-Host "[ERRO] Falha na compilacao do VoxIA.exe" -ForegroundColor Red
}
