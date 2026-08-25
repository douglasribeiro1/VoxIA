@echo off
setlocal

set CSC_PATH=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe

if not exist "%CSC_PATH%" (
    echo [ERRO] Compilador C# nao encontrado em %CSC_PATH%
    exit /b 1
)

echo Compilando VoxIA-v2.exe...

set REFERENCES=/lib:"C:\Program Files\Reference Assemblies\Microsoft\Framework\v3.0" /r:System.dll,System.Drawing.dll,System.Windows.Forms.dll,System.Web.Extensions.dll,System.Net.Http.dll,UIAutomationClient.dll,UIAutomationTypes.dll

set ICON_ARG=
if exist "..\app.ico" (
    set ICON_ARG=/win32icon:..\app.ico
)

"%CSC_PATH%" %REFERENCES% %ICON_ARG% /target:winexe /out:VoxIA-v2.exe /recurse:*.cs

if %ERRORLEVEL% equ 0 (
    echo [SUCESSO] VoxIA-v2.exe gerado com sucesso!
) else (
    echo [ERRO] Falha na compilacao.
)

endlocal
