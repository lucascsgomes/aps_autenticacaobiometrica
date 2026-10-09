# Banco com autenticação facial

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

A API é a única que fala com o banco e com o `face-service`. Os detalhes, os diagramas e as decisões estão em [`docs/`](docs/README.md).

## Estrutura do repositório

| Pasta | Conteúdo |
|---|---|
| `face-service/` | autenticador facial em Python (hoje ainda o menu de terminal original) |
| `docs/` | briefing, arquitetura, modelo de dados, contratos de API, segurança e plano de ondas |

As pastas da solution .NET entram nas próximas ondas do [plano](docs/06-plano-de-ondas.md).

## Como rodar hoje

Só o `face-service` roda por enquanto. As instruções (Python 3.12, download dos modelos e uso do menu) estão em [`face-service/README.md`](face-service/README.md).

## Status

Em construção, por ondas. Onda 1: fundação do repositório, esqueleto dos projetos e CI.

## Limitações

- Dinheiro fictício, sem integração com Pix ou banco real.
- Sem prova de vida na primeira versão: uma foto de uma foto pode ser aceita. Por isso a senha continua valendo como alternativa e há limite de tentativas.
- O vetor facial é dado biométrico sensível (LGPD). Fica restrito no banco e a foto nunca é guardada.
