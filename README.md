# 🎙 VoxIA — Ecossistema Multiplataforma de Ditado por Voz com IA (Groq)

**VoxIA** é uma suíte completa de ditado por voz e transcrição ultra-rápida potencializada por Inteligência Artificial (Groq Whisper + Llama 3.3 / GPT OSS), disponível nativamente para **Windows**, **macOS** e **Android**.

---

## 📂 Estrutura do Repositório

O projeto é modular e organizado por plataformas e áreas funcionais:

```
VoxIA/
├── 📁 windows/                          # Aplicativo Desktop para Windows (C# .NET)
│   ├── src/                             # Código-fonte C# (.NET Framework nativo)
│   ├── data/                            # Banco de dados e configurações portáteis
│   ├── VoxIA.exe                        # Executável nativo (System Tray)
│   ├── build.bat / build.ps1            # Scripts de compilação rápida
│   ├── app.ico                          # Ícone oficial
│   └── test_prompt_fix.ps1              # Teste de persistência de prompts
│
├── 📁 mac/                              # Aplicativo para macOS (Swift / SwiftUI)
│   ├── src/                             # Código-fonte nativo Swift (Menu Bar)
│   ├── build_mac.sh                     # Script de compilação para macOS
│   ├── README_MAC.md                    # Manual e guia da versão Mac
│   └── COMO_TRANSFERIR_VIA_GOOGLE_DRIVE.md
│
├── 📁 android/                          # Teclado Virtual IME para Android (Kotlin)
│   ├── app/                             # Código-fonte Android e recursos Gradle
│   ├── VoxIA-Teclado-Android.apk        # APK compilado pronto para uso
│   ├── install_android_usb.bat / .ps1   # Instalador e ativador automático via USB/ADB
│   ├── GUIA_ANDROID_E_SINCRONIZACAO.md  # Guia de configuração e Supabase Sync
│   └── GUIA_INSTALACAO_USB_ANDROID.md   # Guia detalhado de depuração USB
│
├── 📁 .github/                          # Automações de CI/CD (GitHub Actions)
│   └── workflows/
│       └── build-android.yml            # Compilação automática do APK
│
└── 📄 README.md                         # Este documento de índice geral
```

---

## ⚡ Principais Características por Plataforma

### 🪟 [Windows](windows/)
- **Residente na Área de Notificação (System Tray)**: Ultra-leve (~80 KB), sem dependências pesadas.
- **Atalho Global Universal (`Ctrl + Win`)**: Dite áudio em qualquer janela ou formulário do Windows.
- **Inserção Inteligente com Fallbacks**: UI Automation, Safe Clipboard e SendInput.
- **Modo Portátil**: Execute diretamente de um Pen Drive com dados salvos em `windows/data/`.
- **Dicionário Técnico & Prompt Profiles**: Substituições customizadas em tempo real.

### 🍎 [macOS](mac/)
- **Aplicativo na Barra de Menus (Menu Bar)**: 100% nativo em Swift e SwiftUI.
- **Atalho Global (`Option + Space`)**: Ditado em qualquer app do Mac via Accessibility APIs.
- **Floating HUD & Audio Feedback**: Indicador translúcido de status em tempo real.

### 📱 [Android](android/)
- **Teclado de Entrada IME Customizado**: Dite áudio com Whisper em qualquer aplicativo (WhatsApp, Notas, Navegador).
- **Instalador USB de 1 Clique**: Script PowerShell/Batch que instala o APK, concede permissões de microfone e ativa o teclado via ADB automaticamente.
- **Botão Enviar Instantâneo**: Despache mensagens no WhatsApp diretamente após a fala.

---

## ⌨ Tabela Rápida de Atalhos Globais

| Função | Atalho Windows | Atalho macOS | Atalho Android |
| :--- | :--- | :--- | :--- |
| **🎙 Ditado por Voz** | `Ctrl + Win` | `Option + Space` | Toque no Ícone do Microfone |
| **📖 Adicionar ao Dicionário** | `Ctrl + Shift + W` | `Option + Shift + W` | Tela de Configurações |
| **⏹ Cancelar Gravação** | `Esc` | `Esc` | Cancelar no Teclado |
| **⚙ Configurações** | Duplo clique na bandeja | Ícone `🎙` na Menu Bar | Ícone de Engrenagem no Teclado |

---

## 🛠 Como Compilar

### Windows
```powershell
cd windows
powershell -ExecutionPolicy Bypass -File build.ps1
# Ou clique duas vezes em windows\build.bat
```

### macOS
```bash
cd mac
chmod +x build_mac.sh
./build_mac.sh
```

### Android
```powershell
cd android
powershell -ExecutionPolicy Bypass -File install_android_usb.ps1
# Ou execute via Gradle:
./gradlew assembleDebug
```

---

## 🔒 Privacidade e Segurança

- Todo áudio gravado localmente é protegido e temporário.
- Comunicação direta com a API oficial da Groq (sem intermediários).
- Credenciais e chaves API armazenadas de forma segura e criptografada em cada sistema operacional.
