# Bergamota 🍊

Roda de ações para os arquivos que você arrasta no Windows, inspirada no Tangerine do Mac.
Comece a arrastar arquivos e segure **CapsLock** (ou leve até a borda direita da tela): a roda aparece e você solta numa fatia.

## Fatias disponíveis
- **Ações nativas:** Extrair aqui / Extrair para pasta (WinRAR → 7-Zip → tar), Compactar ZIP, Renomear (PowerRename), Abrir com…, Propriedades, Copiar caminho
- **Menu de contexto:** qualquer item do botão direito do Explorer (o editor lista os itens de um arquivo de exemplo)
- **Programa:** abre um .exe com os arquivos (`{files}` nos argumentos)
- **Enviar para:** um aparelho com LocalSend na mesma rede, ou "escolher na hora"
- **Converter e ajustar…** (fatia fixa embaixo, desligável em Configurações): um painel com o que serve para os itens soltos.
  - *Converter para:* só os formatos possíveis pela extensão. Imagens via ImageMagick (ou o próprio Windows), vídeo/áudio via FFmpeg, documentos/planilhas/apresentações via Office.
  - *Ajustar imagem:* reduzir 50%, 1920 px, 1080 px, versão leve para web, remover metadados e upscale 2×/4× com IA (Upscayl, se instalado; senão Lanczos).
  - *PDF:* juntar, separar páginas, imagens → PDF (PDFsharp 6.1.1, MIT, dentro do app) e comprimir (Ghostscript).
- **Mover para…:** move ou copia para pastas favoritas (Configurações → Pastas favoritas), recentes ou outra pasta, com a janela de progresso do Windows.

## LocalSend
O LocalSend 1.18+ exige certificado do remetente. A Bergamota cria o seu em `%AppData%\Bergamota\identity.pfx`
e usa o SHA-256 dele como identidade. O LocalSend deste próprio PC não aparece como destino.

## Rodar
```bash
dotnet run --project src/Bergamota
```
O app fica na bandeja; o hub abre ao clicar no ícone. Configurações e log ficam em `%AppData%\Bergamota`.

## Instalador
```bash
powershell -ExecutionPolicy Bypass -File tools/build-installer.ps1
```
Gera `dist/Bergamota-Setup-<versão>.exe` (Inno Setup 6, ~50 MB, .NET embutido). Instala por usuário, sem pedir administrador,
e oferece instalar pelo winget só o que faltar: FFmpeg, ImageMagick, 7-Zip, LocalSend e PowerToys.
Ao desinstalar, as configurações em `%AppData%\Bergamota` são mantidas.

## Testes
```bash
dotnet run --project tools/SelfTest -- --net
```
Testa geometria da roda, perfis, ZIP, extração com WinRAR, leitura do menu de contexto e a varredura de rede.
`--powerrename` também abre o PowerRename com dois arquivos de teste.

## Estrutura
- `src/Bergamota/Core` — gatilhos (hooks), ações, menu de contexto do Explorer, ZIP/extração
- `src/Bergamota/Net` — LocalSend v2: escuta passiva + varredura por intervalo/manual + envio
- `src/Bergamota/UI` — roda (vidro com captura desfocada da tela), hub e telas
- `mockup/` — mockup HTML aprovado do visual
