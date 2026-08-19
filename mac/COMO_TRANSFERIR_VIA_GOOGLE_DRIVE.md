# ☁️ Guia Passo a Passo: Como Transferir o VoxIA do Windows para o Mac da sua Esposa via Google Drive

Este guia ensina como enviar com segurança o aplicativo **VoxIA para macOS** a partir do seu notebook Windows para o Macbook da sua esposa utilizando o **Google Drive**.

---

## 📂 PASSO 1: No seu Notebook com Windows (Criar o Arquivo para Envio)

1. No seu notebook Windows, abra a pasta do projeto `happy-hopper`.
2. Selecione a pasta **`mac`** (ou compacte-a em um arquivo `.zip`).
3. Para compactar no Windows:
   - Clique com o botão direito na pasta **`mac`**.
   - Selecione **Enviar para > Pasta compactada (zip)** (ou use WinRAR / 7-Zip).
   - O arquivo gerado será `mac.zip`.

---

## ☁️ PASSO 2: No seu Google Drive (Fazer o Upload)

1. Acesse **[drive.google.com](https://drive.google.com)** no seu navegador.
2. Faça o upload do arquivo `mac.zip` (ou da pasta `mac`).
3. Clique com o botão direito no arquivo no Google Drive -> **Compartilhar** -> Envie o link para a sua esposa (ou acesse a mesma conta no Macbook dela).

---

## 🍏 PASSO 3: No Macbook da sua Esposa (Baixar e Executar)

1. No Macbook da sua esposa, abra o Safari ou Chrome e acesse o **Google Drive**.
2. Faça o download do arquivo `mac.zip`.
3. Na pasta **Downloads** do Mac, dê dois cliques em `mac.zip` para descompactar. Uma pasta chamada **`mac`** será criada.

---

## ⚡ PASSO 4: Compilar e Iniciar no Mac (Processo Rápido de 1 Minuto)

1. No Macbook da sua esposa, abra o aplicativo **Terminal** (pressione `Cmd + Espaço`, digite `Terminal` e dê Enter).
2. Arraste a pasta `mac` para dentro do Terminal ou digite o comando:
   ```bash
   cd ~/Downloads/mac
   ```
3. Execute os seguintes 2 comandos no Terminal:
   ```bash
   chmod +x build_mac.sh
   ./build_mac.sh
   ```
4. A compilação nativa será executada e gerará o executável **`VoxIAMac`**.
5. Para iniciar o aplicativo no Mac:
   ```bash
   ./VoxIAMac
   ```

---

## ⚙ PASSO 5: Configuração Inicial no Mac

1. O ícone de microfone `🎙` aparecerá na **Barra de Menus superior** do Mac (ao lado do relógio).
2. Clique no ícone -> **`⚙ Configurações`** e insira a **Groq API Key**.
3. Na primeira vez em que gravar, o macOS pedirá permissão de **Microfone** e **Acessibilidade** (`Ajustes do Sistema > Privacidade e Segurança > Acessibilidade`). Clique em **Permitir**.
4. Pressione **`Option + Space`** no Macbook da sua esposa para ditar por voz em qualquer programa!
