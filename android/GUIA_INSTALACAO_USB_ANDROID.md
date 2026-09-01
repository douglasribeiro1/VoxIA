# 📱 Guia de Instalação e Uso: Teclado Android IME VoxIA

Este guia explica como instalar e usar o **Teclado VoxIA no seu celular Android via Depuração USB (ADB)**, com recursos de **Envio Direto da Mensagem**, **Edição de Prompts diretamente no teclado**, **Dicionário inteligente** e **Filtros Anti-Alucinação**.

---

## 🚀 1. O que há de novo nesta versão Android?

1. **✈ Botão de Envio Direto (Send Action)**:
   - Você dita sua mensagem e agora pode tocar no botão azul de envio (ícone de avião de papel) diretamente no teclado do VoxIA.
   - O aplicativo despacha a mensagem imediatamente no WhatsApp, Telegram, Direct, e-mails ou formulários, sem que você precise alternar de volta para o teclado padrão (Gboard)!
   - Opção de **Auto-enviar após ditar** disponível nas configurações.

2. **✎ Edição de Prompts no Teclado em Tempo Real**:
   - Não precisa sair do WhatsApp ou abrir outro aplicativo para ajustar seu prompt de IA!
   - Na barra superior do teclado:
     - Toque no seletor para escolher o perfil (Padrão, Pontuação Rígida, Termos Médicos, Tópicos, etc.).
     - Toque em **✎ (Editar)** para ver e modificar as instruções da IA ali mesmo.
     - Toque em **➕ (Novo)** para criar um novo estilo de formatação na hora.
     - Toque em **Salvar & Usar** e a alteração entra em vigor instantaneamente.

3. **📖 Dicionário e Termos Técnicos**:
   - Toque em **📖** no teclado para adicionar substituições rápidas de palavras.
   - O Whisper recebe automaticamente os termos técnicos para não errar nomes difíceis ou termos médicos/jurídicos.

4. **🛡 Filtros Anti-Alucinação e Guardrail Anti-Resposta (do Windows V2)**:
   - Remove automaticamente ruídos como [Música], (risos), [silêncio], legendas por..., obrigado por assistir.
   - Impede que a IA responda a perguntas ditadas como chatbot; ela formata a pergunta com precisão.

---

## 🔌 2. Como Instalar no Celular via Depuração USB

### Passo 1: Ativar a Depuração USB no seu Celular

1. Abra as **Configurações** do seu Android.
2. Vá em **Sobre o telefone** (ou *Informações do software*).
3. Toque **7 vezes seguidas** em **Número da versão** (ou *Versão do SO / MIUI / Build Number*) até aparecer a mensagem *"Você agora é um desenvolvedor!"*.
4. Volte às Configurações e entre em **Sistema > Opções do desenvolvedor** (ou *Configurações adicionais > Opções do desenvolvedor*).
5. Ative a chave **Depuração USB**.
6. *(Específico para Xiaomi / Redmi / POCO)*:
   - Ative também **Instalar via USB** e **Depuração USB (Configurações de segurança)**.

---

### Passo 2: Conectar o Celular ao Computador e Executar o Instalador

1. Conecte seu celular ao computador usando um cabo USB.
2. Olhe para a tela do celular: se surgir o aviso **"Permitir depuração USB?"**, marque a caixinha **Sempre permitir a partir deste computador** e toque em **OK**.
3. No seu computador, dê dois cliques no arquivo:
   `	ext
   install_android_usb.bat
   `
   *(ou abra o PowerShell e execute .\install_android_usb.ps1)*

4. O script fará tudo de forma 100% automática:
   - ✓ Detecta o celular conectado.
   - ✓ Instala o APK do Teclado VoxIA.
   - ✓ Concede a permissão de microfone (RECORD_AUDIO) automaticamente.
   - ✓ Habilita o teclado na lista de métodos de entrada do Android.
   - ✓ Abre o aplicativo na tela do celular para configuração final.

---

## ⚙ 3. Configuração Inicial no Celular

Ao abrir o VoxIA no celular:

1. **Groq API Key**: Cole sua chave da Groq (gsk_...) e toque em **"Salvar Chave Groq"**.
2. **Preferências**: Marque a opção **"Auto-enviar mensagem após ditar"** se desejar que o teclado envie a mensagem automaticamente após você parar de falar.
3. **Sincronização em Nuvem (Opcional)**: Digite seu e-mail e senha do Supabase para baixar todos os seus prompts e termos do dicionário configurados no Windows!
4. **Definir Teclado**: Toque no botão **2. Definir como Teclado Ativo** e selecione o **"Teclado de Voz VoxIA"**.

---

## 🎙 4. Como Usar no Dia a Dia

1. Abra qualquer conversa no WhatsApp, Telegram ou campo de texto.
2. O teclado VoxIA aparecerá com o grande botão central de microfone.
3. **Para ditar**:
   - Toque no botão redondo de microfone.
   - Fale naturalmente.
   - Toque novamente para parar.
4. O áudio é transcrito em milissegundos via Groq Whisper, filtrado pelo dicionário e ajustado pelo seu prompt de IA.
5. **Para enviar**:
   - Toque no botão azul **✈ Enviar** na barra inferior para despachar o texto diretamente!
6. **Para alternar de volta ao teclado tradicional**:
   - Toque no botão do globo 🌐 na barra inferior.
