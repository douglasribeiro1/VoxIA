# 🎙 VoxIA — Ditado por voz com IA

Fale e o texto aparece escrito, já com pontuação e ortografia corrigidas, em **qualquer programa**: prontuário, Word, WhatsApp, navegador, e-mail.

Funciona no **Windows** e no **Android** (e no Mac, para quem sabe compilar). A transcrição é feita pela **Groq**, um serviço de IA gratuito: cada pessoa usa a **sua própria chave**, que se cria em 2 minutos com uma conta Google.

---

## ⬇️ Downloads

| Sistema | Arquivo | |
| :--- | :--- | :--- |
| 🪟 **Windows** 10 / 11 | **[VoxIA-Windows.zip](https://github.com/douglasribeiro1/VoxIA/releases/download/ultima-versao/VoxIA-Windows.zip)** | [Como instalar](#-passo-2a--instalar-no-windows) |
| 📱 **Android** 8 ou mais novo | **[VoxIA-Android.apk](https://github.com/douglasribeiro1/VoxIA/releases/download/ultima-versao/VoxIA-Android.apk)** | [Como instalar](#-passo-2b--instalar-no-android) |
| 🍎 **Mac** | Precisa compilar | [Guia do Mac](mac/README_MAC.md) |

Todas as versões ficam na página **[Releases](https://github.com/douglasribeiro1/VoxIA/releases)**.

> **Resumo:** ① criar a chave grátis na Groq → ② instalar o VoxIA → ③ colar a chave → ④ falar.

---

## 🔑 Passo 1 — Criar sua chave grátis da Groq

O VoxIA usa a Groq para transformar sua voz em texto. Para isso, você precisa de uma **chave de API**, uma espécie de senha que liga o app à sua conta. **É grátis e não pede cartão de crédito.**

1. Abra **[console.groq.com/keys](https://console.groq.com/keys)** (no computador ou no celular).
2. **Crie sua conta / entre:** clique em **Continue with Google** e escolha sua conta Gmail. Também dá para entrar com GitHub ou com e-mail (nesse caso, a Groq manda um link de confirmação para o seu e-mail).
3. Se o site perguntar seu nome ou para que vai usar, responda qualquer coisa (ex.: "uso pessoal") e continue.
4. Na tela **API Keys**, clique em **Create API Key**.
5. Dê um nome para a chave, por exemplo `VoxIA`, e clique em **Submit**.
6. Aparece a chave, um código longo que **começa com `gsk_`**. Clique em **Copy** para copiar.

> ⚠️ **A chave só aparece uma vez.** Cole num lugar seguro (ex.: um bloco de notas) até terminar a instalação. Se perder, é só voltar no passo 4 e criar outra.
>
> 🔒 **Não compartilhe sua chave.** Ela é pessoal, como uma senha: quem tiver a chave usa a sua conta. Se alguém pedir o app, mande o link desta página, não a sua chave.

---

## 🪟 Passo 2A — Instalar no Windows

1. Baixe o **[VoxIA-Windows.zip](https://github.com/douglasribeiro1/VoxIA/releases/download/ultima-versao/VoxIA-Windows.zip)**.
2. Clique com o botão direito no arquivo baixado → **Extrair tudo…** → escolha uma pasta fixa, por exemplo **Documentos**.
   > Não abra o VoxIA de dentro do .zip nem da pasta Downloads, porque ele precisa salvar as configurações na própria pasta.
3. Abra a pasta extraída e dê dois cliques em **VoxIA.exe**.
4. **Aviso do Windows (normal na 1ª vez):** se aparecer "O Windows protegeu o computador", clique em **Mais informações** → **Executar assim mesmo**. Isso acontece porque o VoxIA é um app pequeno, sem assinatura digital paga, e não porque tenha vírus.
5. Aparece a janela **"Primeiros passos"**. Se ainda não tiver a chave, clique em **Sim** para abrir o site da Groq (Passo 1).
6. Na tela de **Configurações**, cole a chave no campo **Chave de API Groq** e clique em **Salvar**.
7. O VoxIA fica rodando no **ícone 🎙 perto do relógio** (bandeja do Windows). Se não aparecer, clique na setinha **^** ao lado do relógio.

### Como usar no Windows

1. Clique no lugar onde quer escrever (prontuário, Word, WhatsApp Web…).
2. Aperte **F8** e fale.
3. Aperte **F8** de novo. Em poucos segundos o texto aparece escrito no lugar.

| O que fazer | Como |
| :--- | :--- |
| Ditar | **F8** (começa) → fale → **F8** (termina) |
| Cancelar a gravação | **Esc** |
| Ensinar uma palavra (nomes, termos técnicos) | Selecione a palavra e aperte **Ctrl + Shift + W** |
| Abrir as configurações | Dois cliques no ícone 🎙 da bandeja |
| Mudar o atalho, iniciar com o Windows, histórico | Configurações |

Comandos de voz (Windows e Android):
- "**abrir tópico** exame físico **fechar tópico**" cria um título em CAIXA ALTA.
- "**apaga isso**" (ou "risca isso"), **desligado por padrão** (ligue em Configurações): seguido de uma pausa, descarta a frase que você acabou de dizer. Ex.: "Febre de 39 graus. **Apaga isso.** Febre de 38 graus." vira "Febre de 38 graus.". Nunca apaga mais de 30 palavras, não dispara no meio de uma frase e vale só para o ditado atual.

---

## 📱 Passo 2B — Instalar no Android

1. No celular, baixe o **[VoxIA-Android.apk](https://github.com/douglasribeiro1/VoxIA/releases/download/ultima-versao/VoxIA-Android.apk)**.
2. Toque no arquivo baixado para instalar.
   - Se aparecer **"Por segurança, seu telefone não pode instalar apps desconhecidos"**, toque em **Configurações** → ative **Permitir desta fonte** → volte e toque em **Instalar**.
   - Se o **Play Protect** avisar que o app não é conhecido, toque em **Mais detalhes** → **Instalar mesmo assim**. O VoxIA não está na Play Store, e por isso aparece esse aviso.
3. Abra o **VoxIA**. Na primeira vez aparece **"Bem-vindo ao VoxIA!"**:
   - Toque em **Criar chave** se ainda não tiver (Passo 1), ou em **Já tenho a chave**.
4. Cole a chave no campo **Chave da Groq** e toque em **Salvar chave**.
5. Permita o uso do **microfone** quando o celular pedir.
6. Escolha **como quer ditar** (pode usar os dois):

**⌨️ Opção 1: Teclado VoxIA**
1. Na tela do VoxIA, no cartão **Teclado VoxIA**, toque em **Ativar** → ligue o **VoxIA** na lista → confirme o aviso.
2. Toque em **Trocar teclado** → escolha **VoxIA**.
3. Em qualquer app, toque num campo de texto → toque no **microfone** do teclado → fale → toque de novo.

**🫧 Opção 2: Balão flutuante** (um botão redondo que fica por cima dos apps e funciona com o seu teclado normal)
1. Na tela do VoxIA, no cartão **Balão flutuante**, toque em **Ativar na Acessibilidade** → toque em **VoxIA** → ligue.
2. Se o Android disser que a configuração é **restrita** (Android 13 ou mais novo): volte ao VoxIA, toque em **Opção bloqueada? Liberar configurações restritas** → nos **três pontinhos ⋮** → **Permitir configurações restritas** → e ligue de novo na Acessibilidade.
3. Toque num campo de texto, toque no balão, fale e toque de novo.

> Os três indicadores no topo da tela do VoxIA (**Teclado · Balão · Chave**) ficam verdes quando está tudo certo.

**📚 O VoxIA aprende com as suas correções (balão):** se você corrigir à mão uma palavra que o balão acabou de escrever (ex.: "desartria" → "disartria"), aparece uma notificação **Ensinar ao VoxIA?**. Toque nela e em **Salvar**: das próximas vezes a palavra sai certa. Dá para desligar em Configurações → **Sugerir palavras corrigidas**.

---

## 🎯 Como o VoxIA acerta mais

- **Dicionário:** as palavras que você ensina vão como vocabulário para o Whisper e para a correção pela IA, que também conserta palavras trocadas por outras de som parecido quando o contexto deixa claro.
- **Áudio tratado antes de enviar:** tira o ruído grave e aumenta o volume quando você fala baixo ou com o microfone longe. O áudio nunca é recortado, para não perder trechos de fala baixa.
- **Ditados longos ficam prontos mais rápido:** enquanto você ainda fala, os trechos já terminados vão sendo transcritos. Ao parar, só falta o último trecho.
- **Microfone no máximo durante o ditado (Windows):** ao começar a falar, o VoxIA sobe o volume de captação do microfone para 100% e devolve o nível original quando você para. Se o som saturar (microfone perto demais), ele baixa sozinho. Bom para quem deixa o fone em cima da mesa. Dá para desligar em Configurações.
- **Envio compactado sem perda** (FLAC): o arquivo fica menor e chega mais rápido, com o mesmo som.

---

## 🍎 Mac

A versão de Mac precisa ser compilada no próprio Mac (pelo Terminal). Veja o **[guia do Mac](mac/README_MAC.md)**. A chave da Groq é a mesma do Passo 1: no menu 🎙 → **Configurações**, cole a chave ou clique em **Criar chave na Groq**. Atalho para ditar: **Option + Espaço**.

---

## ❓ Problemas comuns

| Problema | Solução |
| :--- | :--- |
| **"Chave recusada" / erro 401** | A chave foi copiada errada ou apagada. Crie outra em [console.groq.com/keys](https://console.groq.com/keys) e cole de novo (sem espaços antes ou depois). |
| **Nada acontece ao apertar F8** | Veja se o ícone 🎙 está na bandeja. Outro programa pode estar usando o F8: troque o atalho em Configurações. |
| **O texto não aparece no campo** | Clique dentro do campo antes de ditar. O texto também fica copiado: é só dar **Ctrl + V**. |
| **"Limite atingido" / erro 429** | O plano grátis da Groq tem um limite de uso por minuto e por dia. Espere um pouco e tente de novo; o áudio não se perde (fica em **Áudios pendentes** e é reenviado). |
| **Microfone não grava** | Windows: *Configurações → Privacidade → Microfone* e permita apps da área de trabalho. Android: *Configurações → Apps → VoxIA → Permissões → Microfone*. |
| **Balão sumiu (Android)** | Abra o VoxIA e toque em **Religar na Acessibilidade**. Desative a economia de bateria para o VoxIA. |
| **Antivírus bloqueou o VoxIA.exe** | Falso alarme comum em apps sem assinatura paga. Confira o código SHA-256 (arquivo `VoxIA.exe.sha256.txt`) e libere nas exceções do antivírus. |

---

## 💰 Custo e 🔒 privacidade

- **O VoxIA é gratuito.** A Groq tem um plano gratuito que costuma dar para o uso de uma pessoa. Não é preciso cadastrar cartão.
- **A sua chave fica só no seu aparelho**, guardada de forma criptografada. Ninguém além de você (nem o autor do app) tem acesso a ela.
- **O áudio vai direto do seu aparelho para a Groq**, sem passar por nenhum outro servidor.
- O histórico de ditados fica só no seu aparelho e é apagado após 7 dias (dá para mudar em Configurações → Privacidade).
- Por padrão, vão para a Groq só o áudio e as palavras do seu dicionário (para acertar a grafia). O texto que já está escrito no campo só vai se você ligar essa opção em Privacidade.
- As sugestões de palavras corrigidas (Android) são feitas no próprio celular: o texto do campo não é enviado a lugar nenhum.

---

<details>
<summary><b>🛠 Para desenvolvedores: estrutura do repositório e como compilar</b></summary>

### Estrutura

```
VoxIA/
├── windows/               # App Windows (C# .NET Framework, bandeja do sistema)
├── mac/                   # App macOS (Swift, barra de menus)
├── android/               # Teclado IME + balão flutuante (Kotlin)
├── VoxIA-Compartilhar/    # Pasta pronta para compartilhar (sem chave de API)
└── .github/workflows/     # Compilação automática (APK e EXE) e publicação na Release "ultima-versao"
```

### Compilação automática

A cada push no `master`, o GitHub Actions compila o `VoxIA.exe` e o APK e atualiza a release **[ultima-versao](https://github.com/douglasribeiro1/VoxIA/releases/tag/ultima-versao)**, de onde saem os links de download acima.

### Compilar manualmente

**Windows**
```powershell
cd windows
powershell -ExecutionPolicy Bypass -File build.ps1
```

**macOS**
```bash
cd mac && chmod +x build_mac.sh && ./build_mac.sh
```

**Android**
```bash
cd android && ./gradlew assembleRelease
```

Guias detalhados: [Android e sincronização](android/GUIA_ANDROID_E_SINCRONIZACAO.md) · [Instalação via USB](android/GUIA_INSTALACAO_USB_ANDROID.md) · [Mac](mac/README_MAC.md)

</details>
