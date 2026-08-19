# 🎙 VoxIA — Ditado por Voz Nativo para macOS (Macbook)

O **VoxIA para macOS** é a versão nativa equivalente para o sistema operacional da Apple. Ele fica residente na **Barra de Menus Superior (Menu Bar)** do Mac e permite ditar por voz em **qualquer aplicativo do macOS** (Pages, Word, WhatsApp, Safari, Chrome, Telegram, Notas, etc.) utilizando a combinação de teclas **`Option + Space`** (ou `Cmd + Option`).

---

## ⚡ Principais Características

- **Residente na Barra de Menus (Menu Bar Extra)**: Fica ao lado do relógio do Mac com o ícone `🎙` sem ocupar espaço na Dock.
- **Atalho Global Universal (`Option + Space` / `Cmd + Option`)**: Ativa a gravação instantaneamente em qualquer janela com foco.
- **📖 Dicionário de Palavras & Motor Local (`Option + Shift + W`)**: Selecione a palavra em qualquer app (Safari, Word, Pages, etc.) e aperte `⌥ + ⇧ + W` para cadastrar regras de substituição instantâneas.
- **Zero Perda de Áudio**: Gravações salvas em `~/Library/Application Support/VoxIA/audio_queue/` antes de qualquer envio. Se a internet cair, o áudio é mantido e reenviado automaticamente.
- **Transcrição com Groq Whisper (`whisper-large-v3` / Turbo)**: Máxima precisão e imunidade a ruídos.
- **Correção Ortográfica Rígida (GPT OSS 120B / Llama 3.3 70B / GPT OSS 20B)**: Correção com preservação integral de nomes próprios, dosagens, termos técnicos e médicos.
- **Inserção Direta via macOS Accessibility API & NSPasteboard**: Copia para o clipboard e insere diretamente no campo de texto ativo via `Cmd + V`.
- **Painel Flutuante (HUD)**: Indicador visual não intrusivo no canto da tela (`🎙 Gravando...`, `⏳ Processando...`, `✓ Concluído`).
- **Segurança Keychain**: API Key da Groq armazenada no Cofre de Chaves do macOS.

---

## ⌨ Tabela Rápida de Atalhos (macOS)

| Função | Atalho macOS | Ação |
| :--- | :--- | :--- |
| **🎙 Ditado por Voz** | `Option + Space` (ou `Cmd + Option`) | Iniciar / Encerrar gravação |
| **📖 Adicionar ao Dicionário** | `Option + Shift + W` (`⌥ + ⇧ + W`) | Captura o texto selecionado e abre o diálogo |
| **⌨ Guia de Atalhos** | Menu `🎙 > Guia de Atalhos...` | Abre o lembrete de atalhos a qualquer momento |

---

## 🛠 Como Compilar e Rodar no Mac

No Mac da sua esposa, abra o aplicativo **Terminal** e siga os passos simples:

### 1. Garantir que as Ferramentas de Compilação do Mac estão ativas
```bash
xcode-select --install
```
*(Se já estiver instalado, ele avisará que já existe).*

### 2. Navegar até a pasta e executar a compilação
```bash
cd /caminho/para/pasta/mac
chmod +x build_mac.sh
./build_mac.sh
```

### 3. Iniciar o aplicativo
```bash
./VoxIAMac
```

---

## 🔒 Permissões do macOS Necessárias (Apenas na 1ª Execução)

O macOS exige 2 permissões simples de segurança para gravadores e atalhos globais:

1. **Acessibilidade (Accessibility)**:
   - Vá em `Ajustes do Sistema > Privacidade e Segurança > Acessibilidade`.
   - Adicione e ative o `VoxIAMac` (permite colar o texto digitado automaticamente).

2. **Microfone (Microphone)**:
   - Ao iniciar a gravação pela 1ª vez, o Mac solicitará permissão de acesso ao microfone. Clique em **Permitir**.
