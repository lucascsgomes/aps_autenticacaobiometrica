# Controle de acesso por biometria facial

[![CI](https://github.com/lucascsgomes/aps_autenticacaobiometrica/actions/workflows/ci.yml/badge.svg)](https://github.com/lucascsgomes/aps_autenticacaobiometrica/actions/workflows/ci.yml)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)
![Python 3.12](https://img.shields.io/badge/Python-3.12-3776AB)

Ferramenta de **identificação e autenticação biométrica** (rosto) que restringe o acesso a uma base de dados do **Ministério do Meio Ambiente**: propriedades rurais que usaram agrotóxicos proibidos. As informações têm três níveis:

| Nível | Quem acessa | O que vê |
|---|---|---|
| 1 | todos os servidores cadastrados | município, estado, produto proibido e ano |
| 2 | diretores de divisão | mais: propriedade, quantidade aplicada e área |
| 3 | somente o ministro | mais: proprietário, documento, multa e andamento da investigação |

Os níveis 2 e 3 abrem **só com o rosto**, confirmado na hora, a cada consulta, e o limiar de semelhança é mais rígido quanto mais alto o nível. Todo acesso, aceito ou recusado, entra num relatório de segurança que só o ministro lê. Os dados são inventados.

Trabalho da APS (faculdade).

## Como funciona

1. A pessoa se cadastra e registra o rosto. A foto não é guardada: só um vetor de 128 números, cifrado.
2. **Entra pelo rosto, sem CPF**: o serviço facial compara a foto com todos os rostos cadastrados (identificação 1 para N) e diz quem é. Também dá para entrar com CPF e rosto (1 para 1) ou com CPF e senha.
3. O nível de acesso fica no banco e só um **administrador** muda, no painel `/admin`, confirmando cada mudança com o próprio rosto. Ninguém se promove sozinho. O primeiro administrador vem da configuração (`Admin:Cpfs`) e não pode ser removido pelo painel.
4. Para abrir o nível 2 ou 3, a pessoa mostra o rosto de novo. A senha não abre nível restrito.
5. Tentar abrir um nível acima do seu é recusado e fica no relatório.

## Arquitetura

```
Ministerio.Web (Blazor WASM) ── HTTPS ──> Ministerio.Api (ASP.NET Core) ──> PostgreSQL (nuvem)
                                                     │
                                                     └── HTTPS ──> face-service (Python, FastAPI, OpenCV)
```

| Peça | Tecnologia |
|---|---|
| Reconhecimento facial | **Python**, OpenCV, YuNet (detecção) e SFace (vetor facial), comparação por cosseno |
| API | ASP.NET Core, EF Core, JWT |
| Interface | Blazor WebAssembly, com a câmera do navegador |
| Dados | PostgreSQL (Neon) |

O `face-service` não guarda nada: recebe a foto, devolve o vetor ou a decisão. Quem guarda os vetores (cifrados) e decide o acesso é a API.

## Estrutura do repositório

| Pasta | Conteúdo |
|---|---|
| `face-service/` | a ferramenta biométrica em Python: pipeline de imagem, detecção, vetor, comparação e identificação 1 para N; roda como menu de terminal (`cli.py`) e como serviço HTTP |
| `src/Ministerio.Dominio` | regras de negócio puras (níveis, usuário, desafio biométrico, propriedade rural, registro de acesso) |
| `src/Ministerio.Infra` | EF Core + PostgreSQL, migrations, cliente do face-service, cifra do vetor, desafios, níveis e auditoria |
| `src/Ministerio.Contratos` | DTOs compartilhados entre API e interface (JSON em `snake_case`) |
| `src/Ministerio.Api` | API: cadastro, entradas, níveis de acesso, relatório e erros em Problem Details |
| `src/Ministerio.Web.Compartilhado` | páginas, cliente da API e componente de câmera (Razor Class Library) |
| `src/Ministerio.Web` | casca Blazor WebAssembly |
| `tests/` | testes unitários e de integração (a API sobe inteira em memória, com PostgreSQL real embutido) |

## Como rodar

Não precisa de Docker. Você só precisa de **.NET SDK 10** e **Python 3.12**.

**1. Reconhecimento facial (Python).** Os modelos YuNet e SFace são baixados uma vez (veja o [README do serviço](face-service/README.md)).

```powershell
cd face-service
py -3.12 -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements-dev.txt
.\.venv\Scripts\python.exe -m pytest
$env:FACE_INTERNAL_KEY = "chave-de-desenvolvimento"
.\.venv\Scripts\python.exe -m uvicorn app.main:app --port 7860
```

No Linux e no macOS, troque `.\.venv\Scripts\python.exe` por `.venv/bin/python` e `$env:X = "v"` por `export X=v`. Sem servidor, o menu de terminal faz cadastro, autenticação 1 para 1 e identificação 1 para N com fotos de arquivo: `python cli.py`.

**2. Segredos da API** (ficam no *user-secrets*, nunca no repositório):

```powershell
$chave = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
dotnet user-secrets set "ConnectionStrings:Dados" "Host=...;Database=...;Username=...;Password=...;SSL Mode=Require" --project src/Ministerio.Api
dotnet user-secrets set "Face:BaseUrl" "http://127.0.0.1:7860" --project src/Ministerio.Api
dotnet user-secrets set "Face:ChaveInterna" "chave-de-desenvolvimento" --project src/Ministerio.Api
dotnet user-secrets set "Biometria:ChaveCifra" $chave --project src/Ministerio.Api   # perdeu a chave, perdeu os rostos: guarde fora do banco
dotnet user-secrets set "Admin:Cpfs:0" "<CPF do administrador, só dígitos>" --project src/Ministerio.Api   # 1, 2... para mais administradores fixos
```

Todo cadastro nasce servidor (nível 1). Quem está em `Admin:Cpfs` nasce administrador; os demais níveis e administradores saem do painel. Sem `Face:BaseUrl` o rosto responde `503` (nunca é aceito sem verificação). Com `Face:BaseUrl`, a API exige `Face:ChaveInterna` e `Biometria:ChaveCifra`.

**3. Banco e API.**

```powershell
dotnet tool restore
dotnet ef database update --project src/Ministerio.Infra --startup-project src/Ministerio.Api   # cria as tabelas e carrega os 20 registros fictícios
dotnet run --project src/Ministerio.Api                                                          # http://localhost:5080/ready
```

Sem a connection string a API sobe, mas `/ready` responde 503 (`/health` continua 200). Sem `Jwt:Chave`, em `Development` a API sorteia uma chave a cada início; fora de `Development` a chave é obrigatória.

**4. Interface web.**

```powershell
dotnet run --project src/Ministerio.Web          # http://localhost:5090
```

O endereço da API fica em `src/Ministerio.Web/wwwroot/appsettings.json` (`ApiBaseUrl`) e a API só aceita chamadas das origens de `Cors:Origens` (padrão `http://localhost:5090`). A câmera só abre em `localhost` ou HTTPS.

**5. Testes.**

```powershell
dotnet test Ministerio.sln        # unitários e integração (PostgreSQL 17 embutido, baixado na primeira vez)
```

## Rotas da API

| Rota | O que faz |
|---|---|
| `POST /v1/auth/register` | cadastro em `CADASTRO_PENDENTE`; devolve um `cadastro_token` de 5 minutos |
| `POST /v1/auth/face/enroll` | multipart com a foto (`image`, até 2 MB): guarda o vetor cifrado e conclui o cadastro |
| `POST /v1/auth/register/retomar` | quem parou na foto volta com CPF e senha (`RETOMAR` ou `RECOMECAR`) |
| `POST /v1/auth/login/identificar` | **1 para N**: só a foto; o serviço descobre quem é. `401 FACE_NOT_VERIFIED` se ninguém passa do limiar ou se dois ficam parecidos demais |
| `POST /v1/auth/login/desafio` e `/login/face` | CPF e rosto (1 para 1), com desafio de uso único |
| `POST /v1/auth/login/senha` | CPF e senha |
| `POST /v1/auth/refresh` e `/logout` | renova e encerra a sessão |
| `GET /v1/me` | nome, CPF mascarado, nível, cargo e se tem rosto |
| `GET`, `PUT`, `DELETE /v1/me/biometria` | consultar, trocar (com a senha e uma foto nova) e excluir o rosto (com a senha) |
| `PUT /v1/me/senha` | trocar a senha, com o rosto ou a senha atual; encerra as sessões |
| `POST /v1/biometria/desafios` | abre o desafio de `ACESSO_NIVEL_2`, `ACESSO_NIVEL_3`, `TROCAR_SENHA` ou `RECADASTRO` |
| `GET /v1/dados/1` | informações de nível 1 |
| `POST /v1/dados/2` e `/v1/dados/3` | multipart com `desafio_id` e a foto: abre o nível se a pessoa tem aquele nível e o rosto confere |
| `GET /v1/relatorios/resumo` e `/acessos.csv` | relatório de segurança, só nível 3 |
| `GET /health` e `/ready` | processo vivo; banco acessível |

## Reconhecimento facial

O caminho da foto, em `face-service/biometria/pipeline.py`:

1. a imagem é decodificada pelo OpenCV e reduzida para no máximo 640 px de largura;
2. vai para tons de cinza e passa pelo pré-processamento (CLAHE por padrão; equalização e gaussiano existem, e três tratamentos de ruído também, desligados);
3. o **YuNet** detecta o rosto e 5 pontos (olhos, nariz, cantos da boca). Nenhum rosto vira `NO_FACE_DETECTED` e mais de um vira `MULTIPLE_FACES`;
4. o **SFace** alinha o rosto (112×112) e extrai **128 números**;
5. a comparação é a **similaridade do cosseno** entre dois vetores, e a decisão é `score >= limiar`.

Os modelos YuNet e SFace são pré-treinados (OpenCV Zoo): **não treinamos rede neural**. O trabalho aqui é o pipeline, as regras de decisão, a medição e a integração segura.

| Decisão | Limiar do cosseno |
|---|---|
| entrar (login) | 0,363 (valor recomendado pelo OpenCV para o SFace) |
| abrir o nível 2 | 0,400 |
| abrir o nível 3, trocar rosto ou senha | 0,500 |

Na identificação 1 para N, além do limiar há uma **margem mínima de 0,05** entre o primeiro e o segundo candidato: se duas pessoas ficam parecidas demais, o sistema recusa em vez de escolher. Os valores são configuráveis (`Biometria:LimiarLogin`, `LimiarNivel2`, `LimiarNivel3`, `Margem`).

Medido com fotos públicas reais (uma pessoa e outra): mesma pessoa em outra foto 0,668 e 0,947; pessoas diferentes 0,041 e **0,269**. A distância até o limiar de 0,363 é pequena, e é por isso que o nível 3 usa um limiar mais alto. É uma amostra pequena e sem calibração com uma população.

## Segurança

- **A foto nunca é guardada.** Fica só em memória durante a requisição. O vetor de 128 floats é cifrado com AES-256-GCM, com o id do usuário como dado associado (um vetor copiado para a linha de outra pessoa não decifra); a chave vem de fora do banco e a versão dela fica gravada.
- **O score nunca vai para o cliente** (viraria oráculo), e o relatório também não o guarda.
- **Desafio de uso único (90 s)**: cada abertura de nível 2 ou 3 usa um desafio amarrado ao usuário e à finalidade, consumido por um `UPDATE` atômico. Foto sem rosto, com vários rostos ou serviço fora do ar devolvem o desafio (até 5 vezes); rosto errado gasta o desafio e conta no bloqueio.
- **Falha fechada**: se o `face-service` cai, nenhum rosto é aceito. O cliente do serviço tem timeout, uma repetição e circuit breaker.
- **Bloqueio progressivo**: 5 falhas seguidas bloqueiam por 1 minuto, dobrando até 15.
- **JWT** HS256 de 15 minutos; **refresh token** opaco e rotativo (reuso revoga a família); trocar a senha ou o rosto encerra todas as sessões.
- **Rate limit** (janela de 1 minuto, `429` com `Retry-After`): rotas públicas de biometria 10 por IP, rotas autenticadas de biometria 15 por usuário, cadastro e renovação 20 por IP, leituras 120 por usuário. `RateLimit:Fator` multiplica os limites.
- **Senha** com `PasswordHasher` do ASP.NET Identity, comparação sempre executada para igualar o tempo de resposta.
- **Erros** em Problem Details (RFC 9457) com um `code` estável (`FACE_NOT_VERIFIED`, `NIVEL_INSUFICIENTE`, `FACE_REQUIRED`, `ACCOUNT_LOCKED`...).
- **LGPD**: consentimento específico para biometria, com a versão do termo; o usuário pode trocar ou apagar o próprio rosto.

## Painel de acesso (administrador)

`/admin` lista os cadastrados e deixa o administrador mudar o nível (1, 2 ou 3) e quem é administrador. Cada mudança exige o rosto do administrador (limiar 0,500, desafio de uso único) e entra no relatório como `ALTERACAO_DE_ACESSO`, com o antes e o depois. A mudança vale na hora, sem novo login. Regras: só administrador acessa (`403 ADMIN_REQUIRED`), o administrador da configuração e você mesmo não perdem o perfil de administrador (`409 CHANGE_NOT_ALLOWED`) e o alvo precisa ter o cadastro concluído (`404 USER_NOT_FOUND`).

Rotas: `GET /v1/admin/usuarios` e `PUT /v1/admin/usuarios/{id}/acesso` (multipart: `desafio_id`, `dados` com `nivel` e/ou `admin`, `image`), com desafio aberto antes em `POST /v1/biometria/desafios` (`finalidade: ALTERAR_ACESSO`).

## Relatório de segurança

Cada tentativa (entrada por senha, rosto ou identificação; consulta de cada nível; leitura do relatório) grava quem, quando, o resultado, o motivo e o IP. O ministro vê, em `/relatorio`, os totais (aceitas, recusadas, acima do nível), a contagem por tipo de acesso e por motivo, as últimas 50 tentativas e pode baixar tudo em CSV.

## Publicação (link fixo)

Tudo em plano gratuito: a API e o `face-service` no Render (dois serviços do mesmo `render.yaml`) e a interface no GitHub Pages. O banco é o Neon. O `face-service` usa cerca de 200 MB de memória (medido com fotos reais), dentro dos 512 MB do plano grátis.

1. **API e face-service (Render).** *New > Blueprint*, escolha este repositório (ou cole o link, que é público) e preencha os valores pedidos: `ConnectionStrings__Dados` (a connection string do Neon), `Biometria__ChaveCifra` e `Admin__Cpfs__0` (o CPF do administrador fixo). Para mais, crie `Admin__Cpfs__1`, `__2`... no painel do Render. A chave entre os dois serviços (`FACE_INTERNAL_KEY`) o Render sorteia e copia sozinho.
2. **Ligar a API ao face-service.** Copie a URL do serviço `face-service` e crie, na API, a variável `Face__BaseUrl` (sem barra no fim). O Render reinicia a API.
3. **Interface (GitHub Pages).** Em *Settings > Pages*, escolha *Source: GitHub Actions*; em *Settings > Secrets and variables > Actions > Variables*, crie `API_URL` com a URL da API e rode o workflow "Publicar interface web". A origem do Pages já está liberada no CORS (`Cors__Origens__0`).

Os serviços do plano grátis dormem depois de 15 minutos sem uso, e acordar leva cerca de um minuto cada: antes de uma apresentação, abra `/health` da API e `/internal/v1/health` do face-service. Em produção o atalho `Auth:CadastroSemRosto` é ignorado.

## Integração contínua

A cada push e pull request, o GitHub Actions faz o build da solution (aviso vira erro), roda os testes da API, o `ruff` e o `pytest` do `face-service`, e constrói as imagens Docker do serviço facial (confere `/ready` com os modelos reais) e da API (confere `/health`).

## Limitações

- **Sem detecção de vivacidade**: uma foto de uma foto pode ser aceita. A senha não abre níveis restritos, mas o limite de tentativas e o limiar mais alto nos níveis restritos são o que há contra isso hoje.
- **Limiares sem calibração** com uma população: os valores vêm do padrão do SFace e de medições pequenas.
- **Duas contas com o mesmo rosto** não são impedidas no cadastro; na identificação 1 para N elas se tornam "parecidas demais" e a entrada pelo rosto sem CPF é recusada para as duas (o CPF com rosto continua valendo).
- A identificação 1 para N compara com até 1000 cadastros por chamada.
- Os dados da base são inventados.
- O vetor facial é dado biométrico sensível (LGPD). Fica restrito no banco, cifrado, e a foto nunca é guardada.
