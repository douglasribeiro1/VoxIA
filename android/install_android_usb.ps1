# ==============================================================================
# VoxIA Android - Script de Instalacao e Ativacao via Depuracao USB (ADB)
# ==============================================================================

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   INSTALADOR DO TECLADO VOXIA ANDROID (VIA USB/ADB)     " -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host ""

# Configurar JAVA_HOME se necessario
if (-not $env:JAVA_HOME) {
    $jdkCandidates = @(
        "C:\Program Files\Eclipse Adoptium\jdk-17.0.20.101-hotspot",
        "C:\Program Files\Eclipse Adoptium\jdk-17*"
    )
    foreach ($jdk in $jdkCandidates) {
        $found = Get-Item $jdk -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($found) {
            $env:JAVA_HOME = $found.FullName
            $env:PATH = "$env:JAVA_HOME\bin;$env:PATH"
            break
        }
    }
}

# 1. Localizar o executavel do ADB
$adbPath = $null

if (Get-Command adb -ErrorAction SilentlyContinue) {
    $adbPath = "adb"
} else {
    $candidates = @(
        "$env:LOCALAPPDATA\Android\Sdk\platform-tools\adb.exe",
        "C:\Program Files\Android\platform-tools\adb.exe",
        "C:\Android\platform-tools\adb.exe",
        "$env:USERPROFILE\AppData\Local\Android\Sdk\platform-tools\adb.exe"
    )
    foreach ($path in $candidates) {
        if ($path -and (Test-Path $path)) {
            $adbPath = $path
            break
        }
    }
}

if (-not $adbPath) {
    Write-Host "[!] ADB nao foi encontrado automaticamente." -ForegroundColor Red
    Write-Host "    Certifique-se de que o Android SDK / Platform-Tools esta instalado." -ForegroundColor Yellow
    Write-Host ""
    $manualPath = Read-Host "Digite o caminho completo para o adb.exe (ou pressione Enter para sair)"
    if ($manualPath -and (Test-Path $manualPath)) {
        $adbPath = $manualPath
    } else {
        Write-Host "Operacao cancelada." -ForegroundColor Red
        Exit 1
    }
}

Write-Host "[✓] ADB encontrado: $adbPath" -ForegroundColor Green
Write-Host ""

# 2. Verificar Dispositivos Conectados
Write-Host "[*] Verificando conexao com o celular via USB..." -ForegroundColor Cyan
& $adbPath start-server | Out-Null
$devicesOutput = & $adbPath devices
$deviceLines = ($devicesOutput -split "`r?`n") | Where-Object { $_ -match "\b(device|unauthorized|offline)\b" -and $_ -notmatch "List of devices" }

if (-not $deviceLines) {
    Write-Host ""
    Write-Host "[!] NENHUM CELULAR DETECTADO VIA USB!" -ForegroundColor Red
    Write-Host ""
    Write-Host "Por favor, siga estes passos no seu celular:" -ForegroundColor Yellow
    Write-Host "  1. Conecte o celular ao computador usando um cabo USB de boa qualidade."
    Write-Host "  2. Ative as 'Opcoes do Desenvolvedor' no celular:"
    Write-Host "     (Configuracoes -> Sobre o telefone -> Toque 7 vezes em 'Numero da versao')"
    Write-Host "  3. Entre em 'Opcoes do Desenvolvedor' e ATIVE a 'Depuracao USB'."
    Write-Host "  4. Se for Xiaomi/Redmi/POCO: Ative tambem 'Instalar via USB' e 'Depuracao USB (Configuracoes de Seguranca)'."
    Write-Host ""
    Read-Host "Apos conectar e ativar, pressione ENTER para tentar novamente..."
    
    $devicesOutput = & $adbPath devices
    $deviceLines = ($devicesOutput -split "`r?`n") | Where-Object { $_ -match "\b(device|unauthorized|offline)\b" -and $_ -notmatch "List of devices" }
    if (-not $deviceLines) {
        Write-Host "[x] Nenhum dispositivo detectado. Encerrando." -ForegroundColor Red
        Exit 1
    }
}

# Verifica se esta em estado unauthorized
if ($deviceLines -match "unauthorized") {
    Write-Host ""
    Write-Host "[!] Celular detectado mas precisa de AUTORIZACAO!" -ForegroundColor Yellow
    Write-Host "    Olhe para a tela do seu celular AGORA." -ForegroundColor Cyan
    Write-Host "    Marque a opcao 'Sempre permitir a partir deste computador' e toque em 'Permitir / OK'." -ForegroundColor Cyan
    Write-Host ""
    Read-Host "Apos autorizar no celular, pressione ENTER para continuar..."
}

Write-Host "[✓] Celular conectado e autorizado com sucesso!" -ForegroundColor Green
Write-Host ""

# 3. Localizar o arquivo APK para instalacao
$apkPath = $null
$possibleApks = @(
    "$PSScriptRoot\VoxIA-Teclado-Android.apk",
    "VoxIA-Teclado-Android.apk",
    "$PSScriptRoot\app\build\outputs\apk\debug\app-debug.apk",
    "app\build\outputs\apk\debug\app-debug.apk",
    "$PSScriptRoot\..\android\VoxIA-Teclado-Android.apk",
    "$PSScriptRoot\..\android\app\build\outputs\apk\debug\app-debug.apk",
    "$PSScriptRoot\android\app\build\outputs\apk\debug\app-debug.apk",
    "$PSScriptRoot\output\VoxIA-Teclado-Android.apk",
    "output\VoxIA-Teclado-Android.apk",
    "app-debug.apk"
)

foreach ($p in $possibleApks) {
    if (Test-Path $p) {
        $apkPath = (Resolve-Path $p).Path
        break
    }
}

if (-not $apkPath) {
    Write-Host "[*] APK nao encontrado pronto. Compilando localmente..." -ForegroundColor Cyan
    $gradlewPath = $null
    if (Test-Path "$PSScriptRoot\gradlew.bat") {
        $gradlewPath = "$PSScriptRoot"
    } elseif (Test-Path "$PSScriptRoot\android\gradlew.bat") {
        $gradlewPath = "$PSScriptRoot\android"
    }

    if ($gradlewPath) {
        Push-Location $gradlewPath
        & .\gradlew.bat assembleDebug
        Pop-Location
        $candidateOut = Join-Path $gradlewPath "app\build\outputs\apk\debug\app-debug.apk"
        if (Test-Path $candidateOut) {
            $apkPath = (Resolve-Path $candidateOut).Path
        }
    }
}

if (-not $apkPath) {
    Write-Host ""
    Write-Host "[!] Nenhum arquivo APK foi encontrado para instalacao." -ForegroundColor Yellow
    Write-Host "    Coloque o arquivo 'VoxIA-Teclado-Android.apk' nesta pasta." -ForegroundColor Cyan
    Write-Host ""
    $customApk = Read-Host "Ou informe o caminho completo do APK gerado"
    if ($customApk -and (Test-Path $customApk)) {
        $apkPath = (Resolve-Path $customApk).Path
    } else {
        Write-Host "Instalacao cancelada." -ForegroundColor Red
        Exit 1
    }
}

# 4. Instalar o APK no Celular via ADB
Write-Host "[*] Instalando o Teclado VoxIA no celular ($apkPath)..." -ForegroundColor Cyan
$installResult = & $adbPath install -r -d "$apkPath"
Write-Host $installResult

if ($installResult -match "Success") {
    Write-Host "[✓] APK instalado com sucesso no aparelho!" -ForegroundColor Green
} else {
    Write-Host "[!] Status da instalacao: $installResult" -ForegroundColor Yellow
}

# 5. Conceder Permissao de Gravacao de Audio automaticamente via ADB
Write-Host "[*] Concedendo permissao de gravacao de microfone..." -ForegroundColor Cyan
& $adbPath shell pm grant com.voxia.keyboard android.permission.RECORD_AUDIO 2>$null
Write-Host "[✓] Permissao de audio concedida automaticamente!" -ForegroundColor Green

# 6. Habilitar o Servico de Teclado no Sistema Android
Write-Host "[*] Habilitando o Teclado VoxIA na lista de metodos de entrada do Android..." -ForegroundColor Cyan
& $adbPath shell ime enable com.voxia.keyboard/.service.VoxIAInputMethodService 2>$null

# 7. Abrir a Tela de Configuracoes do VoxIA no celular
Write-Host "[*] Abrindo o app VoxIA na tela do seu celular..." -ForegroundColor Cyan
& $adbPath shell am start -n com.voxia.keyboard/.ui.SettingsActivity 2>$null

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Green
Write-Host "          INSTALACAO CONCLUIDA COM SUCESSO!               " -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
Write-Host ""
Write-Host "Passos finais no seu celular:" -ForegroundColor Yellow
Write-Host "  1. A tela de configuracoes do VoxIA ja foi aberta no aparelho."
Write-Host "  2. Cole sua Groq API Key e toque em 'Salvar Chave Groq'."
Write-Host "  3. (Opcional) Faca login no Supabase para sincronizar seus Prompts e Dicionario."
Write-Host "  4. Toque em '2. Definir como Teclado Ativo' e selecione o 'Teclado de Voz VoxIA'."
Write-Host "  5. Abra o WhatsApp, toque no icone do microfone no teclado e comece a ditar!"
Write-Host "  6. Utilize o botao ENVIAR (aviao de papel) para despachar a mensagem direto!"
Write-Host ""