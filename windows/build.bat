@echo off
cd /d "%~dp0"
echo Compilando VoxIA.exe...
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /lib:"C:\Program Files\Reference Assemblies\Microsoft\Framework\v3.0" /r:System.dll,System.Drawing.dll,System.Windows.Forms.dll,System.Web.Extensions.dll,System.Net.Http.dll,UIAutomationClient.dll,UIAutomationTypes.dll /win32icon:app.ico /target:winexe /out:VoxIA.exe src\Program.cs src\Services\*.cs src\UI\*.cs src\VoxIAContext.cs
if %ERRORLEVEL% EQU 0 (
    echo [SUCESSO] VoxIA.exe gerado com sucesso!
) else (
    echo [ERRO] Falha na compilacao.
)
pause
