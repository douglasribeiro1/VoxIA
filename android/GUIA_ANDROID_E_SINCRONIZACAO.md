# 🎙 Manual Completo: Teclado Android (IME) & Sincronização em Nuvem (VoxIA)

Este guia ensina o passo a passo completo para compilar o **Teclado Android (IME) do VoxIA** utilizando o **GitHub Actions (100% gratuito e na nuvem, sem precisar instalar nada no seu computador)** e como sincronizar seus prompts e dicionários entre o **Windows** e o **Android** através do Supabase com login e senha.

---

## 📱 PARTE 1: Compilação do APK do Teclado Android no GitHub

Você não precisa instalar o Android Studio, Java nem SDKs de 10 GB no seu PC. Todo o processo de compilação acontece nos servidores da nuvem do GitHub.

### Passo 1: Enviar os arquivos para o seu repositório no GitHub

Abra o terminal (PowerShell ou Prompt de Comando) na pasta do VoxIA e execute:

```powershell
git add .
git commit -m "Adiciona Teclado Android IME, compilador GitHub Actions e VoxIA Windows V2"
git push origin main
```
*(Se sua branch principal se chamar `master`, use `git push origin master`)*

---

### Passo 2: Acompanhar a compilação automática na nuvem

1. Acesse o seu repositório no **GitHub** pelo navegador (no PC ou no celular).
2. Clique na aba **"Actions"** no topo da página.
3. Você verá o workflow em execução: **`Compilar Teclado Android (APK)`**.
4. O GitHub levará cerca de **1 a 2 minutos** para compilar tudo automaticamente nos servidores deles.

---

### Passo 3: Baixar o arquivo `.apk` gerado

1. Quando o fluxo terminar (ficar com um ícone verde `✓`), clique sobre a execução finalizada.
2. Role até a seção **"Artifacts"** (na parte inferior da página da execução).
3. Clique em **`VoxIA-Teclado-Android`** para baixar o arquivo ZIP contendo o seu instalador `.apk`.
4. Extraia o arquivo `.apk` e envie para o seu celular (ou acesse o GitHub direto pelo navegador do celular para baixar direto nele).

---

## 📲 PARTE 2: Instalação e Ativação no seu Celular Android

1. **Instalar o APK:**
   * Toque no arquivo `VoxIA-Teclado-Android.apk` no seu celular.
   * Se o Android exibir o aviso *"Instalar aplicativos desconhecidos"*, toque em **Configurações** e permita a instalação pelo seu gerenciador de arquivos/navegador.
   * Toque em **Instalar** e depois em **Abrir**.

2. **Configuração Inicial no Celular:**
   * **Permissão de Microfone:** Conceda a permissão para gravação de áudio quando solicitado.
   * **Groq API Key:** Cole sua chave da Groq (a mesma `gsk_...` que você usa no Windows) e toque em **"Salvar Chave Groq"**.
   * **Login Supabase:** Digite o e-mail e senha da sua conta cadastrada e toque em **"Entrar e Sincronizar"**. O app baixará instantaneamente todos os seus prompts e o dicionário de palavras.

3. **Ativar o Teclado no Sistema:**
   * Toque no botão **`1. Ativar Teclado VoxIA no Android`**. O sistema abrirá a lista de teclados do Android. Ligue a chave do **"Teclado de Voz VoxIA"**.
   * Toque no botão **`2. Definir como Teclado Ativo`** e selecione o **VoxIA**.

4. **Como usar no dia a dia:**
   * Abra qualquer aplicativo (WhatsApp, Bloco de Notas, E-mail, Telegram, etc.).
   * O teclado do VoxIA será exibido na parte inferior da tela.
   * **Para ditar:** Toque no grande botão redondo de microfone, fale o que deseja e toque novamente para parar.
   * O áudio é transcrito pela Groq, formatado pela IA com o seu prompt selecionado, o dicionário é aplicado e o texto corrigido é **digitado diretamente onde seu cursor estiver piscando**!
   * **Para voltar ao teclado normal (Gboard):** Toque no botão do globo `🌐` na barra inferior do teclado.

---

## 💻 PARTE 3: Usando a Nova Versão Windows V2 (`windows-v2/`)

A versão Windows V2 está localizada na pasta `windows-v2/` e possui sincronização nativa com o Supabase.

1. **Executar:**
   * Abra a pasta `windows-v2/` e dê dois cliques no arquivo **`VoxIA-v2.exe`** (já compilado e pronto).
   * Ele ficará minimizado na bandeja do sistema (perto do relógio).

2. **Conectar sua Conta:**
   * Clique com o botão direito no ícone do microfone na bandeja e escolha **`⚙ Configurações`**.
   * Clique na aba **`☁ Nuvem & Android`**.
   * Digite o seu e-mail e senha do Supabase e clique em **`🔑 Entrar e Sincronizar`**.
   * Pronto! O status mudará para `✓ Conectado como: seu-email@exemplo.com`.

3. **Como funciona a sincronização contínua:**
   * Sempre que você cadastrar um novo termo no Dicionário (`Ctrl + Shift + W`) ou criar/editar um Prompt no computador, basta clicar no menu da bandeja **`☁ Sincronizar Nuvem (Supabase)`** ou clicar em **"Sincronizar Agora"** nas configurações.
   * O seu celular Android receberá as atualizações na nuvem automaticamente!

---

## 🗄 PARTE 4: Estrutura do Banco de Dados Supabase

Para referência, as tabelas utilizadas no Supabase são:

### 1. Tabela `prompts`
```sql
create table if not exists prompts (
    id text primary key,
    user_id uuid references auth.users not null default auth.uid(),
    title text not null,
    prompt_text text not null,
    is_builtin boolean default false,
    updated_at timestamp with time zone default timezone('utc'::text, now())
);

alter table prompts enable row level security;

create policy "Usuários podem gerenciar seus próprios prompts"
    on prompts for all
    using (auth.uid() = user_id)
    with check (auth.uid() = user_id);
```

### 2. Tabela `dictionary`
```sql
create table if not exists dictionary (
    id text primary key,
    user_id uuid references auth.users not null default auth.uid(),
    original text not null,
    replacement text not null,
    is_vocabulary_only boolean default false,
    created_at timestamp with time zone default timezone('utc'::text, now())
);

alter table dictionary enable row level security;

create policy "Usuários podem gerenciar seu próprio dicionário"
    on dictionary for all
    using (auth.uid() = user_id)
    with check (auth.uid() = user_id);
```


## Comandos de voz de tópico e título (novo)

- `abrir tópico exame físico fechar tópico` → escreve **EXAME FÍSICO** em linha própria, com o texto logo abaixo.
- `abrir título enfermaria de reabilitação traço clínica médica fechar título` → escreve `ENFERMARIA DE REABILITAÇÃO - CLÍNICA MÉDICA` (caixa alta, sem ponto final), seguido de uma linha em branco. "traço" ou "hífen" viram " - ".
- Os títulos não passam pela IA, então não são alterados. Sem o "fechar", o comando é ignorado.
- O teclado também lê até 400 caracteres antes do cursor (nunca em campos de senha) para acertar a grafia de termos e siglas. O trecho fica só na memória.
- No perfil "Geral / Parágrafos", textos longos (300+ caracteres) são separados em parágrafos por mudança de assunto.
