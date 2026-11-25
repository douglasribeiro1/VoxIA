# 🎙 VoxIA para Mac

O VoxIA fica na **barra de menus** do Mac (ícone 🎙 perto do relógio) e escreve o que você fala em **qualquer app**: Pages, Word, navegador, WhatsApp, prontuário.

É a mesma inteligência das versões Windows e Android:
- **Whisper large-v3** com o vocabulário do seu dicionário e correção pela IA, que conserta palavras trocadas por outras de som parecido quando o contexto deixa claro.
- **Ditados longos** divididos nas pausas e **transcritos enquanto você ainda fala**.
- **Áudio tratado sem cortes:** tira o ruído grave e aumenta a fala baixa, sem nunca recortar trechos.
- **Microfone no máximo durante o ditado:** baixa sozinho se o som saturar e volta ao nível original no fim.
- **Nada se perde:** sem internet, o áudio fica guardado e é reenviado sozinho.
- **Atualização com um clique.**

---

## ⬇️ Instalar

1. Baixe o **[VoxIA-Mac.zip](https://github.com/douglasribeiro1/VoxIA/releases/download/ultima-versao/VoxIA-Mac.zip)** e abra o arquivo (o Mac extrai sozinho).
2. Arraste o **VoxIA** para a pasta **Aplicativos**.
3. **Primeira vez:** clique com o **botão direito** no VoxIA → **Abrir** → **Abrir**. O macOS avisa porque o app não é da App Store; isso só acontece uma vez.
   - Se não aparecer o botão Abrir: **Ajustes do Sistema → Privacidade e Segurança**, role até o aviso do VoxIA e clique em **Abrir Mesmo Assim**.
4. Permita o **microfone** quando o Mac pedir.
5. Permita a **Acessibilidade** (para o atalho e para colar o texto): **Ajustes do Sistema → Privacidade e Segurança → Acessibilidade** → ligue o **VoxIA**.
6. Cole a sua **chave da Groq** (a mesma do Windows e do Android) na tela de configurações que abre na primeira vez.

## ⌨ Como usar

| O que fazer | Como |
| :--- | :--- |
| Ditar | **Option + Espaço** → fale → **Option + Espaço** |
| Ensinar uma palavra | Selecione a palavra e aperte **Option + Shift + W** |
| Configurações, atualizações | Ícone 🎙 na barra de menus |

## 🔄 Atualizações

O VoxIA procura versões novas sozinho. Quando houver, aparece um aviso: abra o menu 🎙 → **Atualizar o VoxIA**. Ele troca o app e abre de novo.

> Como o app não tem assinatura paga da Apple, depois de cada atualização o macOS pode pedir de novo a permissão de **Acessibilidade**: desligue e ligue o VoxIA na lista.

## 🛠 Compilar no próprio Mac (opcional)

```bash
xcode-select --install   # só na primeira vez
./mac/build_mac.sh       # gera mac/build/VoxIA.app
```
Uma cópia compilada à mão não se atualiza sozinha.
