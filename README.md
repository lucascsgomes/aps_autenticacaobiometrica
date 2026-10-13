# Banco com autenticação facial

[![CI](https://github.com/lucascsgomes/aps_autenticacaobiometrica/actions/workflows/ci.yml/badge.svg)](https://github.com/lucascsgomes/aps_autenticacaobiometrica/actions/workflows/ci.yml)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)
![Python 3.12](https://img.shields.io/badge/Python-3.12-3776AB)

Banco digital fictício (dinheiro de mentira) em que o **login** e a **confirmação de transferência** podem ser feitos com **reconhecimento facial** ou, quando a câmera não é possível ou o rosto falha, com a **senha da conta**. O mesmo app roda no navegador e no celular, e o rosto cadastrado fica na nuvem, então vale em qualquer dispositivo sem cadastrar de novo.

Trabalho da APS (faculdade) e projeto de portfólio.

## Como funciona

1. A pessoa se cadastra e registra o rosto (uma biometria por conta).
2. No login, entra com o rosto ou com a senha.
3. Ao transferir, o app pede uma confirmação ligada ao valor e ao destino, resolvida com o rosto ou com a senha.
4. A transferência é lançada no ledger com partida dobrada, de forma atômica.

## Arquitetura

```
Banco.Web (Blazor WASM) ─┐
                         ├─ HTTPS ─> Banco.Api (ASP.NET Core) ─> PostgreSQL (nuvem)
Banco.Mobile (MAUI) ─────┘                   │
                                             └─ HTTPS ─> face-service (FastAPI, OpenCV)
```

| Peça | Tecnologia |
|---|---|
| Front web e mobile | Blazor Hybrid (.NET MAUI + Blazor WebAssembly) com Razor Class Library compartilhada |
| API | ASP.NET Core, EF Core, JWT |
| Banco | PostgreSQL em nuvem |
| Reconhecimento facial | Python, OpenCV, YuNet (detecção) e SFace (vetor facial), comparação por Cosine |

A API é a única que fala com o banco e com o `face-service`. Os detalhes de arquitetura, contratos de API e segurança ficam na documentação de projeto, mantida localmente por enquanto.

## Estrutura do repositório

| Pasta | Conteúdo |
|---|---|
| `src/Banco.Dominio` | regras de negócio puras (`Dinheiro` em centavos, `Usuario`, `Conta`, `Transferencia` com máquina de estados, `Lancamento`) |
| `src/Banco.Infra` | EF Core + PostgreSQL (mapeamento, migrations, `ExecutorDeTransferencia`); depois, o cliente do face-service |
| `src/Banco.Contratos` | DTOs compartilhados entre API e clientes (JSON em `snake_case`) |
| `src/Banco.Api` | API ASP.NET Core: cadastro, login, refresh, conta, health checks e erros em Problem Details |
| `tests/` | testes unitários e de integração (a API sobe inteira em memória, com PostgreSQL real embutido) |
| `face-service/` | microserviço de reconhecimento facial em Python (FastAPI + OpenCV) |

Os projetos de front (Blazor Web e MAUI) entram nas próximas ondas.

## Como rodar

Não precisa de Docker. Você só precisa de **.NET SDK 10** e **Python 3.12**.

**API (.NET)**

```powershell
dotnet test Banco.sln                      # testes unitários e de integração
dotnet run --project src/Banco.Api         # abre http://localhost:5080/ready
```

Os testes de integração sobem um **PostgreSQL 17 embutido** (baixado na primeira execução), então não precisam de Docker nem de conta na nuvem.

**Banco de dados (PostgreSQL na nuvem, ex.: Neon)**

A connection string nunca vai para o repositório. Guarde nos *user-secrets* e aplique as migrations:

```powershell
dotnet user-secrets set "ConnectionStrings:Banco" "Host=...;Database=...;Username=...;Password=...;SSL Mode=Require" --project src/Banco.Api
dotnet tool restore
dotnet ef database update --project src/Banco.Infra --startup-project src/Banco.Api
```

Sem a connection string a API sobe, mas `/ready` responde 503 (`/health` continua 200).

**Testando a API sem o reconhecimento facial**

O cadastro de produção termina com a foto do rosto (próxima seção). Para testar o resto sem câmera, ligue o atalho de desenvolvimento (só vale em `Development`, é ignorado em qualquer outro ambiente):

```powershell
dotnet user-secrets set "Auth:CadastroSemRosto" "true" --project src/Banco.Api
dotnet run --project src/Banco.Api
```

```powershell
$corpo = '{"nome":"Ana Souza","cpf":"11144477735","email":"ana@exemplo.com","senha":"uma-senha-longa-e-unica","consentimento_biometrico":{"aceito":true,"versao_termo":"2026-10-v1"}}'
Invoke-RestMethod http://localhost:5080/v1/auth/register -Method Post -Body $corpo -ContentType application/json   # conta com R$ 5.000 fictícios e sessão aberta
```

Sem `Jwt:Chave` configurada, em `Development` a API sorteia uma chave a cada início (os tokens morrem ao reiniciar). Fora de `Development` a chave é obrigatória e a API não sobe sem ela.

**Cadastro com o rosto de verdade**

Com o atalho desligado, `POST /v1/auth/register` cria o usuário pendente e devolve um `cadastro_token` de 5 minutos; a foto vai em `POST /v1/auth/face/enroll` (multipart, campo `image`, até 2 MB) e é isso que ativa a conta, com o crédito inicial. Para a API falar com o microserviço, guarde a URL, a chave de serviço e a chave que cifra o vetor facial:

```powershell
$chave = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
dotnet user-secrets set "Auth:CadastroSemRosto" "false" --project src/Banco.Api
dotnet user-secrets set "Face:BaseUrl" "http://127.0.0.1:7860" --project src/Banco.Api
dotnet user-secrets set "Face:ChaveInterna" "<a mesma FACE_INTERNAL_KEY do microserviço>" --project src/Banco.Api
dotnet user-secrets set "Biometria:ChaveCifra" $chave --project src/Banco.Api   # perdeu a chave, perdeu os templates: guarde fora do banco
```

Sem `Face:BaseUrl` o rosto responde `503 FACE_SERVICE_UNAVAILABLE` (nunca é aceito sem verificação). Com `Face:BaseUrl` e sem `Biometria:ChaveCifra`, a API recusa subir.

**Microserviço facial (Python)** (os modelos YuNet e SFace são baixados uma vez, veja o [README do serviço](face-service/README.md))

```powershell
cd face-service
py -3.12 -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements-dev.txt
.\.venv\Scripts\python.exe -m pytest
$env:FACE_INTERNAL_KEY = "chave-de-desenvolvimento"
.\.venv\Scripts\python.exe -m uvicorn app.main:app --port 7860
```

No Linux e no macOS, troque `.\.venv\Scripts\python.exe` por `.venv/bin/python` e `$env:X = "v"` por `export X=v`.

## Como o dinheiro é protegido

- **Dinheiro em centavos** (`long`), nunca ponto flutuante.
- **Ledger de partida dobrada**: cada transferência grava um débito e um crédito em `lancamentos`. O saldo da conta é só um cache, e a conciliação confere que ele bate com a soma do ledger.
- **O banco impõe as regras, não só o código**: saldo de cliente nunca negativo (`CHECK`), lançamentos imutáveis (trigger bloqueia `UPDATE`, `DELETE` e `TRUNCATE`), transições de estado válidas (trigger, com histórico) e débitos iguais a créditos conferidos no `COMMIT` (constraint trigger deferida).
- **Concorrência**: o `ExecutorDeTransferencia` trava a transferência e as duas contas com `SELECT ... ORDER BY id FOR UPDATE`. A ordem fixa evita deadlock entre A para B e B para A, e uma repetição cobre `40P01` e `40001`.
- **Provado em teste, em PostgreSQL real**: 50 transferências simultâneas de R$ 10 contra R$ 100 concluem exatamente 10, e 400 transferências cruzadas terminam sem perder um centavo. Toda conciliação ao fim dos cenários dá zero.

## Autenticação

| Rota | O que faz |
|---|---|
| `POST /v1/auth/register` | Cria usuário (`CADASTRO_PENDENTE`) e conta, e registra o consentimento da biometria (LGPD). A conta só ativa quando o rosto for cadastrado |
| `POST /v1/auth/face/enroll` | Recebe a foto com o `cadastro_token`, pede o vetor ao microserviço, cifra e guarda, ativa a conta, lança o crédito inicial e abre a sessão. Foto sem rosto ou com vários rostos (`422`) não gasta o cadastro: dá para tentar de novo até o token vencer |
| `POST /v1/auth/register/retomar` | Para quem parou o cadastro no rosto: prova a posse com CPF e senha do cadastro e escolhe `RETOMAR` (novo `cadastro_token`, mesmos dados) ou `RECOMECAR` (troca nome, e-mail, celular, senha e consentimento do mesmo usuário e da mesma conta). Resposta uniforme para CPF inexistente e senha errada; só quem acerta a senha descobre que o cadastro já foi concluído (`409`) |
| `POST /v1/auth/login/desafio` | Primeiro passo do login por rosto, só com o CPF. Responde `200` com a mesma forma exista o CPF ou não, tenha a conta rosto ou não |
| `POST /v1/auth/login/face` | Recebe `desafio_id` e a foto. O desafio vale 90 s e uma tentativa de verdade: rosto recusado o gasta. Foto sem rosto ou com vários, ou serviço facial fora do ar, devolvem o desafio (até 5 devoluções por foto ruim) |
| `POST /v1/auth/login/senha` | Entrada por CPF e senha. Resposta idêntica para CPF inexistente, senha errada e cadastro pendente; 5 falhas bloqueiam por 1, 2, 4, 8 e até 15 minutos, e o bloqueio só aparece a quem acerta a senha |
| `POST /v1/auth/refresh` | Troca o refresh token por um novo. Usar um token já usado revoga a família inteira |
| `POST /v1/auth/logout` | Revoga a família do refresh token |
| `GET /v1/contas/me` | Dados, saldo e chaves (mascaradas) da conta do usuário do token. A conta nunca vem da URL, então um usuário não alcança a de outro |
| `GET /v1/contas/me/extrato` | Lançamentos com saldo após cada linha e a contraparte mascarada. Cursor opaco `(criado_em, id)` sobre o índice do ledger, sem `OFFSET`; filtros `limit` (1 a 100), `de`, `ate` (fuso `America/Sao_Paulo`) e `tipo` (`D` ou `C`) |
| `GET /v1/chaves/{valor}` | Consulta de destino: devolve só o nome mascarado (`Maria S***`) e se a chave é própria. `422 INVALID_KEY_FORMAT` e `404 KEY_NOT_FOUND` |
| `DELETE /v1/contas/me/chaves/{id}` | Apaga uma chave da própria conta. Chave alheia responde igual a chave inexistente |
| `POST /v1/transferencias/intencoes` | Grava o que será confirmado (chave, valor, descrição), sem mover dinheiro. Checa destino, saldo e a própria conta, e devolve o resumo com o nome mascarado do destinatário. Vale 90 segundos, e uma intenção nova expira a anterior |
| `POST /v1/transferencias/{id}/confirmar` | Confirma com a senha e exige `Idempotency-Key`. Retry com a mesma chave devolve a resposta original sem mover dinheiro de novo; a mesma chave em outra transferência dá `422` |
| `POST /v1/transferencias/{id}/desafio` | Abre o desafio de rosto da transferência (só quem tem rosto cadastrado). Abrir de novo devolve o mesmo desafio, sem zerar as devoluções já gastas |
| `POST /v1/transferencias/{id}/confirmar-face` | Confirma com o rosto: `desafio_id` e a foto em multipart, mais `Idempotency-Key`. Limiar 0,450 (`Biometria:LimiarTransferencia`). Rosto errado cancela a transferência (`401 FACE_NOT_VERIFIED`, estado `FALHA`); foto sem rosto ou com vários (`422`) e serviço facial fora do ar (`503`) deixam a transferência pendente e devolvem o desafio |
| `GET /v1/transferencias/{id}` | Estado e histórico, para a tela de "status incerto" quando a resposta do confirmar se perde |
| `GET /v1/transferencias/{id}/comprovante` | Comprovante com E2E ID e código de autenticação simulado, para quem enviou e para quem recebeu (que vê a origem mascarada e o próprio saldo) |
| `GET /v1/contas/me/limites` | Limites diurno e noturno, quanto já foi usado na janela atual e se ela é `DIURNA` ou `NOTURNA` |
| `PUT /v1/contas/me/limites` | Reduzir vale na hora. Aumentar um limite ou encurtar o período noturno responde `403 STEP_UP_REQUIRED` (`finalidade=ALTERAR_LIMITE`) até a confirmação por rosto ou senha existir |

- **Limites**: padrão de R$ 5.000 por dia e R$ 1.000 à noite (20h às 6h, ou 22h às 6h), no fuso `America/Sao_Paulo`. O diurno soma o dia civil inteiro e o noturno soma só a janela da noite. A intenção faz uma checagem de cortesia, e a que vale roda dentro da transação do dinheiro, com a conta de origem travada: transferências simultâneas não passam juntas pelo limite (provado em teste: 5 de R$ 300 contra R$ 1.000 concluem exatamente 3). Estourar vira `422 NIGHT_LIMIT_EXCEEDED` ou `DAILY_LIMIT_EXCEEDED`.
- **Confirmação por senha**: senha errada gasta a intenção (`FALHA`) e conta para o bloqueio da conta. Senha certa de conta bloqueada recebe `403 ACCOUNT_LOCKED` e não move nada. A confirmação trava a linha da transferência, então confirmações simultâneas movem o dinheiro uma vez só (provado em teste).
- **Rosto no cadastro**: a foto nunca é guardada (fica só em memória, e a API força o buffer do formulário acima de 2 MB para o ASP.NET não gravar em disco). O vetor de 128 floats é cifrado com AES-256-GCM, com o id do usuário como dado associado (template copiado para outra linha não decifra), a chave vem de fora do banco e a versão dela fica gravada. Um usuário tem no máximo um template, garantido por índice único. O token de cadastro tem escopo próprio: não abre nenhuma rota de conta, e um token de sessão não serve para enviar o rosto.
- **Login por rosto**: o desafio é consumido por um `UPDATE ... WHERE usado_em IS NULL AND expira_em > agora RETURNING`, então dois usos simultâneos nunca passam os dois. A API decifra o template, pede a comparação ao `face-service` no limiar 0,363 (`Biometria:LimiarLogin`) e abre a sessão com `metodo=FACE`. O score existe só dentro da API: nenhuma resposta o devolve, porque score na mão do cliente vira oráculo. Desafio inexistente, vencido, usado, CPF sem conta, conta pendente, conta sem rosto e rosto que não bate respondem todos `401 FACE_NOT_VERIFIED`, e nos casos sem o que comparar o serviço facial nem é chamado. Rosto errado conta no bloqueio progressivo junto com a senha errada, e rosto certo de conta bloqueada só então descobre o `403 ACCOUNT_LOCKED`.
- **Rosto na transferência**: o desafio nasce amarrado à operação (dono, transferência, contas e valor, num hash SHA-256 gravado na abertura) e só é consumido se tudo isso bater, então um desafio de outra transferência ou de outra pessoa não vale e não cancela nada. O limiar é mais rígido que o do login (0,450 contra 0,363) porque aqui há dinheiro. A intenção oferece `FACE` entre os métodos só para quem tem rosto cadastrado, e a senha continua sempre valendo. O comprovante registra o método (`FACE` ou `SENHA`).
- **Falha fechada**: o cliente do microserviço tem timeout de 5 s por tentativa, 10 s no total, uma repetição e circuit breaker. Chave interna errada, modelos fora, 5xx ou resposta que a API não entende viram `503 FACE_SERVICE_UNAVAILABLE` com `Retry-After`.
- **JWT** HS256 de 15 minutos, validado com emissor, audiência, expiração e algoritmo fixo (`alg=none` e assinatura adulterada são recusados em teste).
- **Refresh token** opaco de 32 bytes; no banco fica só o SHA-256, e cada login abre uma família rotativa de 7 dias.
- **Senha** com `PasswordHasher` do ASP.NET Identity, e comparação sempre executada para igualar o tempo de resposta.
- **Chaves de transferência**: todo cadastro cria a chave CPF. O valor é único no banco inteiro, o formato é checado na aplicação e por `CHECK`, e a tela nunca recebe o CPF completo. Cadastrar e-mail, celular e chave aleatória exige confirmação por rosto ou senha e entra junto com a biometria.
- **Rate limit** (janela de 1 minuto, `429` com `Retry-After` e `code=RATE_LIMITED`): `login/senha` 5 por IP, `register`, `refresh` e `logout` 20 por IP, consulta de chave 10 e leituras 120 por usuário. `RateLimit:Fator` multiplica os limites (os testes usam 1000). Atrás de proxy, como no Render, defina `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`, senão todos os clientes chegam com o IP do proxy e dividem o mesmo limite.
- **Erros** em Problem Details (RFC 9457) com um `code` estável (`INVALID_CREDENTIALS`, `ACCOUNT_LOCKED`, `REGISTRATION_CONFLICT`...).

## Integração contínua

A cada push e pull request, o GitHub Actions faz o build da solution (aviso vira erro), roda os testes da API, o `ruff` e o `pytest` do `face-service`, e constrói a imagem Docker do serviço para conferir `/ready` com os modelos reais. O Dependabot sugere atualização semanal de NuGet, pip e Actions.

## Status

Em construção, por ondas. Pronto até aqui: repositório e esteira de CI, solution .NET com primeira regra de domínio testada, API no ar com health checks, o autenticador facial transformado em microserviço HTTP com enroll e verify, o PostgreSQL com usuários, contas, transferências e ledger de partida dobrada (regras como constraints e triggers, provadas sob concorrência), e a API com cadastro, login por senha, JWT, refresh rotativo, extrato por cursor, chaves de transferência, rate limit, transferência confirmada por senha com idempotência, limites diurno e noturno, o rosto no cadastro, a retomada do cadastro, o login por rosto e a confirmação da transferência com o rosto. Próximo: a confirmação reforçada nas ações sensíveis (limite, chave, senha, rosto).

## Limitações

- Dinheiro fictício, sem integração com Pix ou banco real.
- Sem prova de vida na primeira versão: uma foto de uma foto pode ser aceita. Por isso a senha continua valendo como alternativa e há limite de tentativas.
- O vetor facial é dado biométrico sensível (LGPD). Fica restrito no banco e a foto nunca é guardada.
