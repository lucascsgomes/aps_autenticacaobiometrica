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

O cadastro de produção termina com a foto do rosto. Enquanto essa parte não existe, ligue o atalho de desenvolvimento (só vale em `Development`, é ignorado em qualquer outro ambiente):

```powershell
dotnet user-secrets set "Auth:CadastroSemRosto" "true" --project src/Banco.Api
dotnet run --project src/Banco.Api
```

```powershell
$corpo = '{"nome":"Ana Souza","cpf":"11144477735","email":"ana@exemplo.com","senha":"uma-senha-longa-e-unica","consentimento_biometrico":{"aceito":true,"versao_termo":"2026-10-v1"}}'
Invoke-RestMethod http://localhost:5080/v1/auth/register -Method Post -Body $corpo -ContentType application/json   # conta com R$ 5.000 fictícios e sessão aberta
```

Sem `Jwt:Chave` configurada, em `Development` a API sorteia uma chave a cada início (os tokens morrem ao reiniciar). Fora de `Development` a chave é obrigatória e a API não sobe sem ela.

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
| `POST /v1/auth/login/senha` | Entrada por CPF e senha. Resposta idêntica para CPF inexistente, senha errada e cadastro pendente; 5 falhas bloqueiam por 1, 2, 4, 8 e até 15 minutos, e o bloqueio só aparece a quem acerta a senha |
| `POST /v1/auth/refresh` | Troca o refresh token por um novo. Usar um token já usado revoga a família inteira |
| `POST /v1/auth/logout` | Revoga a família do refresh token |
| `GET /v1/contas/me` | Dados e saldo da conta do usuário do token (a conta nunca vem da URL, então um usuário não alcança a de outro) |

- **JWT** HS256 de 15 minutos, validado com emissor, audiência, expiração e algoritmo fixo (`alg=none` e assinatura adulterada são recusados em teste).
- **Refresh token** opaco de 32 bytes; no banco fica só o SHA-256, e cada login abre uma família rotativa de 7 dias.
- **Senha** com `PasswordHasher` do ASP.NET Identity, e comparação sempre executada para igualar o tempo de resposta.
- **Erros** em Problem Details (RFC 9457) com um `code` estável (`INVALID_CREDENTIALS`, `ACCOUNT_LOCKED`, `REGISTRATION_CONFLICT`...).

## Integração contínua

A cada push e pull request, o GitHub Actions faz o build da solution (aviso vira erro), roda os testes da API, o `ruff` e o `pytest` do `face-service`, e constrói a imagem Docker do serviço para conferir `/ready` com os modelos reais. O Dependabot sugere atualização semanal de NuGet, pip e Actions.

## Status

Em construção, por ondas. Pronto até aqui: repositório e esteira de CI, solution .NET com primeira regra de domínio testada, API no ar com health checks, o autenticador facial transformado em microserviço HTTP com enroll e verify, o PostgreSQL com usuários, contas, transferências e ledger de partida dobrada (regras como constraints e triggers, provadas sob concorrência), e a API com cadastro, login por senha, JWT e refresh rotativo. Próximo: extrato, chaves, rate limit e a transferência com confirmação, depois o reconhecimento facial.

## Limitações

- Dinheiro fictício, sem integração com Pix ou banco real.
- Sem prova de vida na primeira versão: uma foto de uma foto pode ser aceita. Por isso a senha continua valendo como alternativa e há limite de tentativas.
- O vetor facial é dado biométrico sensível (LGPD). Fica restrito no banco e a foto nunca é guardada.
