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
| `src/Banco.Web.Compartilhado` | Razor Class Library com as páginas, o cliente da API e a câmera; é o que o app de celular vai reaproveitar |
| `src/Banco.Web` | casca Blazor WebAssembly que hospeda a biblioteca acima |
| `tests/` | testes unitários e de integração (a API sobe inteira em memória, com PostgreSQL real embutido) |
| `face-service/` | microserviço de reconhecimento facial em Python (FastAPI + OpenCV) |

O app de celular (MAUI) entra numa onda posterior.

## Como rodar

Não precisa de Docker. Você só precisa de **.NET SDK 10** e **Python 3.12**.

**API (.NET)**

```powershell
dotnet test Banco.sln                      # testes unitários e de integração
dotnet run --project src/Banco.Api         # abre http://localhost:5080/ready
```

Os testes de integração sobem um **PostgreSQL 17 embutido** (baixado na primeira execução), então não precisam de Docker nem de conta na nuvem.

**Interface web (Blazor WebAssembly)**

Com a API e o microserviço facial no ar (acima), suba a interface em outro terminal e abra http://localhost:5090:

```powershell
dotnet run --project src/Banco.Web          # http://localhost:5090
```

O endereço da API fica em `src/Banco.Web/wwwroot/appsettings.json` (`ApiBaseUrl`, padrão `http://localhost:5080`) e a API só aceita chamadas das origens listadas em `Cors:Origens` (padrão `http://localhost:5090`). A câmera só abre em `localhost` ou HTTPS, e o navegador pede permissão na primeira vez. Hoje há cadastro com a foto do rosto (e retomada do cadastro que parou na foto), entrada com senha ou com o rosto, saldo, chaves, extrato e transferência: a tela mostra o resumo com o nome mascarado do destinatário e pede a confirmação com o rosto (câmera) ou com a senha, e termina no comprovante. A tela de perfil (`/perfil`) cuida do rosto (cadastrar, trocar com a senha e uma foto nova, excluir com a senha), da senha, dos limites (reduzir é direto, aumentar pede confirmação) e das chaves (criar pede confirmação, apagar não); trocar a senha ou o rosto encerra as sessões e leva de volta à entrada. Senha ou rosto errados cancelam a transferência e a tela volta ao começo; foto sem rosto ou serviço facial fora do ar mantêm a transferência pendente para tirar outra foto.

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
| `PUT /v1/contas/me/limites` | JSON reduz na hora. Aumentar um limite ou encurtar o período noturno responde `403 STEP_UP_REQUIRED` (`finalidade=ALTERAR_LIMITE`); para aumentar, abra o desafio em `biometria/desafios` e repita o `PUT` em multipart: `dados` (os limites), `desafio_id` e `image` ou `senha` |
| `POST /v1/biometria/desafios` | Abre o desafio de uma ação sensível: `finalidade` (`ALTERAR_LIMITE`, `NOVA_CHAVE`, `TROCAR_SENHA` ou `RECADASTRO`) e `parametros` com a mesma forma do pedido que ele vai confirmar (as duas últimas não levam parâmetros). Responde `201` com `desafio_id` e validade de 90 s |
| `POST /v1/contas/me/chaves` | Cria uma chave `EMAIL`, `CELULAR` ou `ALEATORIA` (o servidor gera o UUID). Sempre exige confirmação: multipart com `dados`, `desafio_id` e `image` ou `senha`; sem desafio, `403 STEP_UP_REQUIRED` (`NOVA_CHAVE`). Até 5 chaves por conta (`422 KEY_LIMIT_REACHED`); valor em uso dá `409 KEY_ALREADY_EXISTS`. A posse do e-mail ou do celular não é verificada |
| `GET /v1/me/biometria` | Se há rosto cadastrado, desde quando e o modelo. Nunca devolve o vetor |
| `PUT /v1/me/biometria` | Troca o rosto (ou cadastra de novo depois de excluir). Desafio `RECADASTRO`; multipart com `desafio_id`, `nova_image` e a credencial, que é `image` (rosto atual) ou `senha`. Foto nova sem rosto ou com vários (`422`) não gasta o desafio. Encerra todas as sessões |
| `DELETE /v1/me/biometria` | Apaga o rosto. Exige só a senha, em JSON (`{"senha": ...}`), para quem perdeu o rosto conseguir apagar. Sem rosto cadastrado, `409 BIOMETRIA_NOT_REGISTERED` |
| `PUT /v1/me/senha` | Troca a senha. Desafio `TROCAR_SENHA`; multipart com `dados` (`nova_senha`, 10 a 128 caracteres), `desafio_id` e `image` ou `senha` (a atual). Encerra todas as sessões |

- **Limites**: padrão de R$ 5.000 por dia e R$ 1.000 à noite (20h às 6h, ou 22h às 6h), no fuso `America/Sao_Paulo`. O diurno soma o dia civil inteiro e o noturno soma só a janela da noite. A intenção faz uma checagem de cortesia, e a que vale roda dentro da transação do dinheiro, com a conta de origem travada: transferências simultâneas não passam juntas pelo limite (provado em teste: 5 de R$ 300 contra R$ 1.000 concluem exatamente 3). Estourar vira `422 NIGHT_LIMIT_EXCEEDED` ou `DAILY_LIMIT_EXCEEDED`.
- **Confirmação por senha**: senha errada gasta a intenção (`FALHA`) e conta para o bloqueio da conta. Senha certa de conta bloqueada recebe `403 ACCOUNT_LOCKED` e não move nada. A confirmação trava a linha da transferência, então confirmações simultâneas movem o dinheiro uma vez só (provado em teste).
- **Rosto no cadastro**: a foto nunca é guardada (fica só em memória, e a API força o buffer do formulário acima de 2 MB para o ASP.NET não gravar em disco). O vetor de 128 floats é cifrado com AES-256-GCM, com o id do usuário como dado associado (template copiado para outra linha não decifra), a chave vem de fora do banco e a versão dela fica gravada. Um usuário tem no máximo um template, garantido por índice único. O token de cadastro tem escopo próprio: não abre nenhuma rota de conta, e um token de sessão não serve para enviar o rosto.
- **Login por rosto**: o desafio é consumido por um `UPDATE ... WHERE usado_em IS NULL AND expira_em > agora RETURNING`, então dois usos simultâneos nunca passam os dois. A API decifra o template, pede a comparação ao `face-service` no limiar 0,363 (`Biometria:LimiarLogin`) e abre a sessão com `metodo=FACE`. O score existe só dentro da API: nenhuma resposta o devolve, porque score na mão do cliente vira oráculo. Desafio inexistente, vencido, usado, CPF sem conta, conta pendente, conta sem rosto e rosto que não bate respondem todos `401 FACE_NOT_VERIFIED`, e nos casos sem o que comparar o serviço facial nem é chamado. Rosto errado conta no bloqueio progressivo junto com a senha errada, e rosto certo de conta bloqueada só então descobre o `403 ACCOUNT_LOCKED`.
- **Rosto na transferência**: o desafio nasce amarrado à operação (dono, transferência, contas e valor, num hash SHA-256 gravado na abertura) e só é consumido se tudo isso bater, então um desafio de outra transferência ou de outra pessoa não vale e não cancela nada. O limiar é mais rígido que o do login (0,450 contra 0,363) porque aqui há dinheiro. A intenção oferece `FACE` entre os métodos só para quem tem rosto cadastrado, e a senha continua sempre valendo. O comprovante registra o método (`FACE` ou `SENHA`).
- **Ações sensíveis (step-up)**: o desafio nasce com um hash de dono, finalidade, parâmetros em JSON canônico (chaves em ordem) e o nonce do próprio desafio. Quem confirma recalcula o hash a partir do que veio no pedido, então o rosto ou a senha aprovados valem para aquele limite ou aquela chave e mais nada. Rosto vale no limiar de 0,450; a senha também serve, e erro de qualquer um dos dois conta no bloqueio e gasta o desafio. Foto sem rosto, com vários ou serviço fora do ar devolvem o desafio.
- **Rosto e senha da conta**: trocar o rosto ou a senha passa pelo step-up e, no mesmo passo, revoga todas as famílias de refresh token (os access tokens já emitidos valem até vencer, 15 minutos). Excluir o rosto não exige o rosto: a senha basta. Sem rosto, a conta continua com a senha e o login e a transferência por rosto voltam depois de um novo cadastro.
- **Falha fechada**: o cliente do microserviço tem timeout de 5 s por tentativa, 10 s no total, uma repetição e circuit breaker. Chave interna errada, modelos fora, 5xx ou resposta que a API não entende viram `503 FACE_SERVICE_UNAVAILABLE` com `Retry-After`.
- **JWT** HS256 de 15 minutos, validado com emissor, audiência, expiração e algoritmo fixo (`alg=none` e assinatura adulterada são recusados em teste).
- **Refresh token** opaco de 32 bytes; no banco fica só o SHA-256, e cada login abre uma família rotativa de 7 dias.
- **Senha** com `PasswordHasher` do ASP.NET Identity, e comparação sempre executada para igualar o tempo de resposta.
- **Chaves de transferência**: todo cadastro cria a chave CPF. O valor é único no banco inteiro, o formato é checado na aplicação e por `CHECK`, e a tela nunca recebe o CPF completo. Cadastrar e-mail, celular e chave aleatória exige confirmação por rosto ou senha e entra junto com a biometria.
- **Rate limit** (janela de 1 minuto, `429` com `Retry-After` e `code=RATE_LIMITED`): `login/senha` 5 por IP, `register`, `refresh` e `logout` 20 por IP, consulta de chave 10 e leituras 120 por usuário. `RateLimit:Fator` multiplica os limites (os testes usam 1000). Atrás de proxy, como no Render, defina `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`, senão todos os clientes chegam com o IP do proxy e dividem o mesmo limite.
- **Erros** em Problem Details (RFC 9457) com um `code` estável (`INVALID_CREDENTIALS`, `ACCOUNT_LOCKED`, `REGISTRATION_CONFLICT`...).

## Publicação (link fixo)

Tudo em plano gratuito: a API e o `face-service` no Render (dois serviços do mesmo `render.yaml`) e a interface no GitHub Pages. O banco é o Neon, que já está na nuvem. O `face-service` usa cerca de 200 MB de memória (medido com fotos reais), dentro dos 512 MB do plano grátis. A ordem importa, porque cada passo precisa do endereço do anterior.

1. **API e face-service (Render).** Crie a conta com o GitHub, depois *New > Blueprint*, escolha este repositório e preencha os dois valores pedidos: `ConnectionStrings__Banco` (a connection string do Neon) e `Biometria__ChaveCifra` (32 bytes em base64; `dotnet user-secrets list --project src/Banco.Api` mostra a do seu computador, use a mesma para manter os rostos já cadastrados). Clique em *Apply*; o Render constrói os dois serviços (uns 5 a 10 minutos). A chave entre os dois serviços (`FACE_INTERNAL_KEY`) o Render sorteia e copia sozinho.
2. **Ligar a API ao face-service.** Abra o serviço `face-service` no painel do Render, copie a URL dele (`https://face-service-xxxx.onrender.com`) e confira que `/internal/v1/ready` responde `200`. No serviço `banco-api`, vá em *Environment* e crie `Face__BaseUrl` com essa URL, sem barra no fim; o Render reinicia a API. Anote a URL da API e confira `/ready`.
3. **Interface (GitHub Pages).** Em *Settings > Pages* do repositório, escolha *Source: GitHub Actions*. Em *Settings > Secrets and variables > Actions > Variables*, crie `API_URL` com a URL da API e rode, em *Actions*, o workflow "Publicar interface web". O site fica em `https://<usuario>.github.io/aps_autenticacaobiometrica/`. A origem do Pages já está liberada no CORS da API (`Cors__Origens__0` no `render.yaml`).

Os serviços do plano grátis dormem depois de 15 minutos sem uso, e acordar leva cerca de um minuto cada (a API acorda o face-service na primeira foto, então conte uns dois minutos). Antes da apresentação, abra `/health` da API e `/internal/v1/health` do face-service e espere responderem. Em produção o atalho `Auth:CadastroSemRosto` é ignorado, então todo cadastro termina com a foto.

## Integração contínua

A cada push e pull request, o GitHub Actions faz o build da solution (aviso vira erro), roda os testes da API, o `ruff` e o `pytest` do `face-service`, e constrói as imagens Docker do serviço facial (confere `/ready` com os modelos reais) e da API (confere `/health`). O Dependabot sugere atualização semanal de NuGet, pip e Actions.

## Status

Em construção, por ondas. Pronto até aqui: repositório e esteira de CI, solution .NET com primeira regra de domínio testada, API no ar com health checks, o autenticador facial transformado em microserviço HTTP com enroll e verify, o PostgreSQL com usuários, contas, transferências e ledger de partida dobrada (regras como constraints e triggers, provadas sob concorrência), e a API com cadastro, login por senha, JWT, refresh rotativo, extrato por cursor, chaves de transferência, rate limit, transferência confirmada por senha com idempotência, limites diurno e noturno, o rosto no cadastro, a retomada do cadastro, o login por rosto e a confirmação da transferência com o rosto. a confirmação por rosto ou senha para aumentar limite e criar chave. Também prontos: a troca de senha e a troca ou exclusão do rosto. A interface web já cadastra com a foto, entra com senha ou rosto e mostra saldo, extrato, transferência e a tela de perfil. Próximo: testar com um rosto de verdade e calibrar os limiares, depois o app de celular. Favorecidos ficam para depois.

## Roteiro da apresentação

Com a API, o `face-service` e a interface web no ar (seção "Como rodar") e `Auth:CadastroSemRosto` em `false`:

1. **Cadastro com o rosto:** em `/cadastro`, preencha os dados, aceite o termo e tire a foto. A conta nasce com R$ 5.000 fictícios.
2. **Login por rosto:** saia, e em `/entrar` informe o CPF e toque em "Entrar com o rosto". Mostre que a senha também funciona.
3. **Transferência com rosto:** em `/transferir`, envie para a chave de outra conta e confirme com o rosto. O comprovante mostra "Confirmada com o rosto".
4. **O que a biometria protege:** com um rosto diferente (ou de costas), a confirmação é recusada e a transferência é cancelada. Em `/perfil`, aumentar o limite ou criar uma chave pede o rosto ou a senha.
5. **Gestão do rosto:** em `/perfil`, trocar e excluir o rosto (LGPD), e trocar a senha.

## Verificação com fotos reais

O `face-service` real (YuNet + SFace) foi rodado contra fotos públicas de exemplo do repositório `face_recognition` (as fotos não ficam neste repositório). Cadastrada uma foto de uma pessoa, a comparação com outras fotos deu:

| Foto comparada | Score | Limiar de login (0,363) | Limiar de transferência (0,450) |
|---|---|---|---|
| a mesma foto | 1,000 | aceita | aceita |
| mesma pessoa, outra foto | 0,668 | aceita | aceita |
| mesma pessoa, foto menor | 0,947 | aceita | aceita |
| outra pessoa | 0,041 | recusa | recusa |

É uma amostra pequena (uma pessoa, três fotos, uma outra pessoa), com fotos boas. Com webcam, luz ruim e ângulo ruim os scores caem, então os limiares ainda precisam ser conferidos com as pessoas que vão usar o sistema.

## Limitações

- Dinheiro fictício, sem integração com Pix ou banco real.
- Sem prova de vida na primeira versão: uma foto de uma foto pode ser aceita. Por isso a senha continua valendo como alternativa e há limite de tentativas.
- O vetor facial é dado biométrico sensível (LGPD). Fica restrito no banco e a foto nunca é guardada.
