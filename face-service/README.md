# Biometria facial

Cadastro e autenticação facial por imagens locais, usando Python, OpenCV, YuNet e SFace. Você escolhe a operação, o identificador e a foto pelo terminal.

## Antes de começar

Baixe ou clone o projeto e abra sua pasta no VS Code. Use **Terminal → Novo Terminal**. Todos os comandos abaixo devem ser executados na pasta que contém `main.py` e `requirements.txt`.

É necessário Python **3.12** instalado (64 bits no Windows). Se ainda não tiver, instale antes de continuar: [downloads do Python](https://www.python.org/downloads/). No Linux, a instalação também deve incluir suporte a `venv`; o pacote correspondente depende da distribuição.

Faça os passos da **primeira execução** em ordem, uma única vez em cada computador. Se algum comando falhar, resolva a falha antes de seguir. A instalação e os downloads precisam de internet.

## Linux — primeira execução

**1. Entre na pasta do projeto e confira o Python.** Substitua o caminho pelo local onde você salvou o projeto:

```bash
cd "/caminho/para/biometria_facial"
python3.12 --version
```

O segundo comando deve mostrar `Python 3.12.x`. Se não encontrar o comando, instale essa versão antes dos próximos passos.

**2. Crie o ambiente virtual e instale as dependências:**

```bash
python3.12 -m venv .venv
.venv/bin/python -m pip install -r requirements.txt
```

**3. Crie as pastas e baixe os dois modelos:**

```bash
mkdir -p modelos imagens
curl --fail --location --output modelos/face_detection_yunet_2023mar.onnx https://media.githubusercontent.com/media/opencv/opencv_zoo/main/models/face_detection_yunet/face_detection_yunet_2023mar.onnx
curl --fail --location --output modelos/face_recognition_sface_2021dec.onnx https://media.githubusercontent.com/media/opencv/opencv_zoo/main/models/face_recognition_sface/face_recognition_sface_2021dec.onnx
```

Espere cada download terminar. Se `curl` não estiver instalado, use os links da seção Modelos e salve os dois arquivos na pasta `modelos/`.

**4. Coloque suas fotos em `imagens/`.** Use uma foto para cadastrar e outra para autenticar a mesma pessoa. Os nomes são livres. O rosto deve estar de frente, alinhado e próximo à câmera.

**5. Só agora inicie o programa:**

```bash
.venv/bin/python main.py
```

## Windows — primeira execução (PowerShell)

Use o **PowerShell**, inclusive no terminal do VS Code, para os comandos abaixo.

**1. Entre na pasta do projeto e confira o Python.** Substitua o caminho:

```powershell
cd "C:\caminho\para\biometria_facial"
py -3.12 --version
```

O segundo comando deve mostrar `Python 3.12.x`. Se `py` não for reconhecido, execute `python --version`. Se ele mostrar Python 3.12, use `python -m venv .venv` no próximo passo. Caso contrário, instale Python 3.12 e reabra o terminal.

**2. Crie o ambiente virtual e instale as dependências:**

```powershell
py -3.12 -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements.txt
```

**3. Crie as pastas e baixe os dois modelos:**

```powershell
New-Item -ItemType Directory -Force -Path modelos, imagens | Out-Null
Invoke-WebRequest -Uri 'https://media.githubusercontent.com/media/opencv/opencv_zoo/main/models/face_detection_yunet/face_detection_yunet_2023mar.onnx' -OutFile 'modelos/face_detection_yunet_2023mar.onnx'
Invoke-WebRequest -Uri 'https://media.githubusercontent.com/media/opencv/opencv_zoo/main/models/face_recognition_sface/face_recognition_sface_2021dec.onnx' -OutFile 'modelos/face_recognition_sface_2021dec.onnx'
```

Espere cada download terminar. Os modelos são os mesmos usados no Linux.

**4. Coloque suas fotos em `imagens/`.** Use uma foto para cadastro e outra para autenticação. Não há nome obrigatório; o rosto deve estar de frente, alinhado e próximo à câmera.

**5. Só agora inicie o programa:**

```powershell
.\.venv\Scripts\python.exe main.py
```

Cada computador precisa criar sua própria `.venv`. Não copie a do Linux para o Windows. No VS Code, escolha `.venv\Scripts\python.exe` como interpretador no Windows. Os comandos acima já usam esse executável diretamente: não é necessário ativar o ambiente ou mudar a política do PowerShell.

## Próximas execuções

Depois de concluir a instalação, abra o terminal na pasta do projeto e execute **somente** o comando do seu sistema. Não precisa repetir a instalação nem os downloads.

Linux:

```bash
.venv/bin/python main.py
```

Windows (PowerShell):

```powershell
.\.venv\Scripts\python.exe main.py
```

## Como usar o menu

No menu:

```text
1 - Cadastrar
2 - Autenticar
0 - Sair
```

Escolha `1`, digite o nome/identificador e o caminho da foto. Depois escolha `2`, informe o mesmo identificador e outra foto para autenticar. Fotos podem ter qualquer nome e estar em qualquer pasta acessível; caminhos com espaços e aspas externas são aceitos. Exemplos: `imagens/retrato.jpg` ou `C:\Users\Rafael\Pictures\foto.jpg`. No menu, caminhos relativos são procurados primeiro na raiz do projeto e depois em `imagens/`. Se a foto estiver em `imagens/`, basta digitar seu nome, como `retrato.jpg`. A pasta `imagens/` é opcional e pode ser criada por você.

Use uma foto com exatamente um rosto visível. Se nenhum rosto for encontrado, o programa orientará a tirar outra foto de frente, com o rosto alinhado e próximo à câmera. Falhas internas mostram uma mensagem simples, sem detalhes técnicos no terminal; quando já existe uma pasta de etapas, o detalhe é mantido em `execucao.txt`. Identificadores distinguem maiúsculas de minúsculas; espaços externos são removidos. Duplicatas não sobrescrevem cadastros.

**Os cadastros ficam apenas na memória enquanto o programa estiver aberto.** Ao sair, são perdidos. Para autenticar, é necessário cadastrar antes na mesma sessão. Arquivos em `resultados/` não são um banco de cadastros.

## Configuração

Em `config.py`:

- `PRE_PROCESSAMENTO_ATUAL`: `clahe` (padrão), `equalizacao` ou `gaussiano`.
- `TRATAMENTO_RUIDO_ATUAL`: `None` (padrão, pula a etapa), `mediana`, `abertura` ou `fechamento`.

O fluxo usa grayscale nos filtros e converte para BGR antes de detectar e alinhar. Ao mudar o processamento, reinicie a sessão e refaça os cadastros. Os filtros não garantem melhoria do reconhecimento.

Decisões internas:

- YuNet: confiança mínima de detecção **0.6**, NMS 0.3 e top-k 5000. Um limite menor pode detectar mais rostos, mas também produzir falsas detecções.
- Exatamente um rosto é exigido; múltiplos rostos geram erro.
- SFace: alinhamento com `alignCrop()` e características com `feature()`.
- Autenticação com `match()` e Cosine: aceita **score >= 0.363**. Quanto maior, mais similares são os vetores.

A confiança do detector (0.6) e o limite da autenticação (0.363) têm funções diferentes. Reduzir a primeira não altera o segundo.

## Resultados

A pasta `resultados/` é criada automaticamente. Cada operação usa o nome da foto, identificador e operação:

```text
resultados/retrato__Rafael__cadastro/
resultados/retrato__Rafael__autenticacao/
resultados/retrato__Rafael__autenticacao__2/
```

Sufixos numéricos evitam sobrescritas. Caracteres incompatíveis são substituídos por `_` e cada parte do nome é limitada a 60 caracteres.

São salvas a imagem original, grayscale, pré-processamento, tratamento de ruído (somente se habilitado), detecção com caixa/landmarks e rosto alinhado. `execucao.txt` registra os métodos e possíveis erros. Em falhas, ficam apenas as etapas alcançadas. Os vetores permanecem em memória.

## Modelos

Os comandos de instalação baixam os binários do repositório oficial OpenCV Zoo. Para download manual:

- [face_detection_yunet_2023mar.onnx](https://media.githubusercontent.com/media/opencv/opencv_zoo/main/models/face_detection_yunet/face_detection_yunet_2023mar.onnx)
- [face_recognition_sface_2021dec.onnx](https://media.githubusercontent.com/media/opencv/opencv_zoo/main/models/face_recognition_sface/face_recognition_sface_2021dec.onnx)

Coloque ambos em `modelos/` com esses nomes exatos. Não salve a página HTML ou o ponteiro textual Git LFS. Se o download for interrompido, repita-o antes de executar. Os comandos de download substituem os arquivos de destino.

Consulte as licenças nas pastas oficiais do [YuNet](https://github.com/opencv/opencv_zoo/tree/main/models/face_detection_yunet) e [SFace](https://github.com/opencv/opencv_zoo/tree/main/models/face_recognition_sface). O processamento biométrico é local, sem internet após a instalação.

## Arquivos do projeto

- `main.py`: menu e entradas manuais.
- `sistema.py`: cadastro, autenticação e resultados.
- `config.py`: configuração do processamento.
- `erros.py`: exceção de domínio.
- `banco/memoria.py`: cadastros temporários.
- `biometria/`: modelos, detector, reconhecimento, comparação e pipeline.
- `processamento/`: filtros e validação de parâmetros.
- `visualizacao/etapas.py`: gravação das imagens intermediárias.
- `requirements.txt`: OpenCV headless 4.11.0.86 e NumPy 2.2.6.
- `.gitignore`: exclui ambiente virtual, caches, modelos, fotos, resultados e configurações locais.

As pastas Python usam pacotes de namespace, sem precisar de arquivos `__init__.py` vazios. Execute `main.py` pela raiz do projeto conforme os comandos acima.

## Limitações

Projeto acadêmico local, sem persistência, frontend, API ou prova de vida. Fotos com rosto pequeno, desfoque, oclusão ou iluminação ruim podem falhar. O funcionamento foi verificado em Linux; a execução em Windows ainda precisa ser confirmada nesse sistema. Fotos reconhecidas em testes manuais não estabelecem acurácia para uso em produção.
