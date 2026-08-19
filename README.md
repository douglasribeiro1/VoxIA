# 🎙 VoxIA — Sistema Global de Ditado por Voz para Windows (Groq API)

**VoxIA** é uma aplicação nativa e ultra-leve para Windows 10 e 11 que funciona como um sistema global de ditado por voz residente na Área de Notificação (System Tray). Inspirado no Wispr Flow, o VoxIA permite ditar áudio em **qualquer campo de texto do Windows** utilizando o atalho global **`CTRL + WIN`**.

---

## ⚡ Principais Características

- **Atalho Global Universal (`Ctrl + Win`)**: Funciona em qualquer programa (Word, WhatsApp Desktop, Chrome, Edge, Firefox, Excel, Outlook, Telegram, Discord, Prontuários Médicos, Bloco de Notas, Editores de Código, etc.).
- **📖 Dicionário de Palavras & Motor Local (`Ctrl + Shift + W`)**: Cadastro instantâneo de palavras e termos técnicos (como *desartria* $\rightarrow$ *disartria*). O motor local executa em $<1\text{ms}$ com preservação automática de maiúsculas/minúsculas (*Case-Matching*).
- **Sem Roubo de Foco**: O aplicativo permanece 100% em segundo plano. O foco permanece exatamente no campo onde você estava digitando.
- **Regra de Ouro (Zero Perda de Áudio)**: O áudio gravado é salvo no disco local **antes** de qualquer tentativa de envio. Se a internet cair, o áudio fica retido na fila persistente e é enviado automaticamente assim que a conexão retornar.
- **Portátil (USB Flash Drive Ready)**: Pode ser executado a partir de um Pen Drive em qualquer computador Windows sem necessidade de instalador! Ao ativar o **Modo Portátil**, as configurações, dicionário (`dictionary.json`), fila e chaves são salvas na pasta `./data/` junto ao executável.
- **Transcrição Ultra-Rápida com Groq Whisper**: Integração com a API oficial da Groq (`whisper-large-v3-turbo` ou `whisper-large-v3`).
- **Correção Ortográfica Rígida (GPT OSS 120B / Llama 3.3 70B / GPT OSS 20B)**: Correção automática de pontuação, acentuação e erros de fala, preservando integralmente nomes próprios, dosagens, termos técnicos e médicos.
- **Inserção Inteligente com Fallbacks**:
  1. *Windows UI Automation* (Inserção direta via ValuePattern);
  2. *Safe Clipboard Paste* (Backup do clipboard original, `Ctrl+V`, restauração segura);
  3. *SendInput Injection* (Simulação direta de eventos de teclado Unicode).
- **Cópia Automática para Clipboard**: O texto final corrigido é copiado imediatamente para a Área de Transferência.
- **Segurança Máxima de Credenciais**: A API Key da Groq é criptografada utilizando o **Windows DPAPI** (`ProtectedData`).
- **Iniciar com o Windows**: Opção para iniciar automaticamente minimizado na bandeja do sistema ao ligar o computador.
- **Indicador Flutuante (HUD)**: Indicador visual não intrusivo (`WS_EX_NOACTIVATE`) exibindo o estado real: `🎙 Gravando...`, `⏳ Processando...`, `✓ Concluído`, `⚠ Falha — áudio salvo`.
- **Sinal Sonoro (Beep Feedback)**: Sons discretos de confirmação de início e fim de gravação.

---

## ⌨ Tabela Rápida de Atalhos Globais

| Função | Atalho Windows | Atalho macOS | Ação |
| :--- | :--- | :--- | :--- |
| **🎙 Ditado por Voz** | `Ctrl + Win` | `Option + Space` | Iniciar / Encerrar gravação |
| **📖 Adicionar ao Dicionário** | `Ctrl + Shift + W` *(ou `Ctrl+Shift+D`)* | `Option + Shift + W` | Captura a palavra selecionada e abre o diálogo |
| **⏹ Cancelar Gravação** | `Esc` | `Esc` | Descarta o áudio gravado |
| **⚙ Configurações / Menu** | Duplo clique no ícone | Ícone `🎙` na barra de menus | Abre a interface de gerenciamento |

---

## 🛠 Como Compilar

O VoxIA foi desenvolvido para compilar nativamente em qualquer máquina Windows sem necessidade de instalar SDKs externos, utilizando o próprio compilador C# (`csc.exe`) nativo do .NET Framework do Windows.

### Opção 1: Via PowerShell (Recomendado)
```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

### Opção 2: Via Prompt de Comando (CMD)
```cmd
build.bat
```

Após a compilação, o executável **`VoxIA.exe`** (apenas ~80 KB) será gerado na raiz da pasta.

---

## 🚀 Como Usar

1. Execute o **`VoxIA.exe`**. Ele ficará residente na bandeja do Windows (System Tray).
2. Clique com o botão direito no ícone de microfone na bandeja e selecione **`⚙ Configurações`** (ou **`🔑 Configurar API Key`**).
3. Cole sua **Groq API Key** (obtenha gratuitamente em [console.groq.com](https://console.groq.com)).
4. Abra qualquer aplicativo (ex: WhatsApp, Word, Bloco de Notas).
5. Clique no campo onde deseja escrever.
6. Pressione **`CTRL + WIN`** para iniciar a gravação. Fale normalmente.
7. Pressione **`CTRL + WIN`** novamente para encerrar.
8. O texto será transcrito pelo Whisper, corrigido pelo Llama 3.3 e inserido automaticamente no seu campo ativo!

---

## 🧰 Modo Portátil (Pen Drive)

Para levar o VoxIA num pen drive e usar em qualquer computador sem deixar rastros no sistema:
1. Ative a opção **"Modo Portátil"** nas Configurações OU crie uma pasta chamada `data` no mesmo diretório do `VoxIA.exe`.
2. Todos os arquivos de configuração (`settings.json`), histórico (`history.json`), chaves criptografadas e fila de áudios serão salvos em `./data/`.

---

## ⚙ Estrutura do Projeto

```
c:\Users\drgom\Documents\antigravity\happy-hopper\
├── VoxIA.exe               # Executável nativo compilado (~80 KB)
├── build.bat                  # Script de compilação em lote
├── build.ps1                  # Script de compilação em PowerShell
├── README.md                  # Manual do usuário
└── src/
    ├── Program.cs             # Ponto de entrada com trava de instância única (Mutex)
    ├── VoxIAContext.cs     # Contexto da aplicação System Tray e fluxo de eventos
    ├── Services/
    │   ├── AppPathService.cs  # Gerenciador de caminhos (Portátil vs %APPDATA%)
    │   ├── AutoStartService.cs# Registro de inicialização no Windows Startup
    │   ├── GlobalHotkeyService.cs # Hook de Teclado Low-Level (Ctrl + Win)
    │   ├── TargetAppTracker.cs# Captura da janela e elemento com foco ativo
    │   ├── AudioRecorder.cs   # Gravação de áudio WAV 16kHz Mono via winmm.dll
    │   ├── TranscriptionService.cs # Cliente HTTP para API Groq Whisper
    │   ├── TextCorrectionService.cs# Cliente HTTP para API Groq Llama 3.3
    │   ├── TextInsertionService.cs # Inserção de texto (UI Automation / Clipboard / SendInput)
    │   ├── ClipboardService.cs# Gerenciador de Área de Transferência
    │   ├── SecureCredentialService.cs # Criptografia DPAPI de chaves
    │   ├── PendingAudioRepository.cs  # Fila persistente de gravação offline
    │   ├── HistoryRepository.cs       # Histórico e estatísticas de uso
    │   ├── RetryService.cs    # Motor de retentativa automática offline
    │   ├── AudioFeedbackService.cs    # Sinais sonoros discretos (Beep)
    │   ├── SettingsService.cs # Gerenciador de configurações JSON
    │   └── Logger.cs          # Logs de diagnóstico sanitizados
    └── UI/
        ├── FloatingHudOverlay.cs # HUD flutuante não-intrusivo
        ├── SettingsForm.cs    # Interface de Configurações
        ├── PendingAudiosForm.cs # Interface da Fila de Áudios Pendentes
        └── HistoryForm.cs     # Interface de Histórico e Pesquisa
```

---

## 🔒 Privacidade e Segurança

- Nenhum áudio ou texto é enviado para servidores de terceiros (somente para a API oficial da Groq).
- A API Key nunca é gravada em plain text; é protegida com criptografia DPAPI associada à sua conta de usuário no Windows.
- O arquivo de áudio local é **excluído imediatamente** assim que a transcrição e correção forem concluídas com sucesso.
