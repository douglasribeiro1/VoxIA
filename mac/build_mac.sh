#!/bin/bash

echo "========================================================"
echo "   Compilando VoxIA para macOS (Native Apple Silicon & Intel)"
echo "========================================================"
echo ""

if ! command -v swiftc &> /dev/null
then
    echo "[ERRO] O compilador 'swiftc' não foi encontrado."
    echo "Instale o Xcode ou ferramentas de linha de comando com:"
    echo "xcode-select --install"
    exit 1
fi

echo "Compilando arquivos Swift..."

swiftc -O \
    -parse-as-library \
    src/AppPathService.swift \
    src/Logger.swift \
    src/SecureCredentialService.swift \
    src/SettingsService.swift \
    src/DictionaryService.swift \
    src/AudioRecorderService.swift \
    src/TextInsertionService.swift \
    src/TranscriptionService.swift \
    src/TextCorrectionService.swift \
    src/PendingAudioRepository.swift \
    src/HistoryRepository.swift \
    src/RetryService.swift \
    src/GlobalHotkeyService.swift \
    src/FloatingHudOverlay.swift \
    src/VoxIAMacApp.swift \
    -o VoxIAMac

if [ $? -eq 0 ]; then
    echo ""
    echo "========================================================"
    echo "   [SUCESSO] VoxIAMac foi gerado com sucesso!"
    echo "========================================================"
    echo "Para rodar o aplicativo no Mac:"
    echo "./VoxIAMac"
    echo ""
else
    echo "[ERRO] Falha na compilação do VoxIA no macOS."
    exit 1
fi
