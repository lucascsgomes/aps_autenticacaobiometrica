using System.Security.Claims;
using System.Text.Json;
using Banco.Api.Seguranca;
using Banco.Contratos;
using Banco.Dominio;
using Banco.Dominio.Biometria;
using Banco.Dominio.Chaves;
using Banco.Infra.Autenticacao;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Banco.Api.Endpoints;

/// <summary>O que vem junto de uma ação sensível: os dados dela, o desafio e a credencial (rosto ou senha).</summary>
internal sealed record Confirmacao(string? Dados, Guid DesafioId, string? Senha, byte[]? Imagem);

internal static class StepUpEndpoints
{
    public static IEndpointRouteBuilder MapearStepUp(this IEndpointRouteBuilder rotas)
    {
        rotas.MapGroup("/v1/biometria").RequireAuthorization().RequireRateLimiting(Limites.Biometria)
            .MapPost("/desafios", AbrirDesafio);
        return rotas;
    }

    private static async Task<IResult> AbrirDesafio(
        DesafioDeAcaoRequest? corpo,
        HttpContext http,
        ClaimsPrincipal usuario,
        ServicoDeStepUp stepUp,
        IOptions<JsonOptions> json,
        CancellationToken cancelamento)
    {
        if (!Guid.TryParse(usuario.FindFirstValue(JwtRegisteredClaimNames.Sub), out var usuarioId))
        {
            return Results.Unauthorized();
        }

        var opcoes = json.Value.SerializerOptions;
        var parametros = corpo?.Parametros is { ValueKind: JsonValueKind.Object } p ? p.GetRawText() : null;
        FinalidadeDoDesafio finalidade;
        string instrucao;
        string canonico;
        Dictionary<string, string[]> erros;
        switch (corpo?.Finalidade)
        {
            case "ALTERAR_LIMITE":
                var limites = Ler<LimitesRequest>(parametros, opcoes);
                (finalidade, instrucao, canonico, erros) = (FinalidadeDoDesafio.AlterarLimite, "Confirme com o rosto ou com a senha a alteração dos limites.", AcoesSensiveis.Canonico(limites), AcoesSensiveis.ErrosDeLimites(limites, out _));
                break;
            case "NOVA_CHAVE":
                var chave = Ler<NovaChaveRequest>(parametros, opcoes);
                (finalidade, instrucao, canonico, erros) = (FinalidadeDoDesafio.NovaChave, "Confirme com o rosto ou com a senha a nova chave.", AcoesSensiveis.Canonico(chave), AcoesSensiveis.ErrosDeChave(chave, out _));
                break;
            case "TROCAR_SENHA":
                (finalidade, instrucao, canonico, erros) = (FinalidadeDoDesafio.TrocarSenha, "Confirme com o rosto ou com a senha atual a troca de senha.", AcoesSensiveis.SemParametros, []);
                break;
            case "RECADASTRO":
                (finalidade, instrucao, canonico, erros) = (FinalidadeDoDesafio.Recadastro, "Confirme com o rosto atual ou com a senha a troca do rosto.", AcoesSensiveis.SemParametros, []);
                break;
            default:
                return Validacao.Problema(new Dictionary<string, string[]> { ["finalidade"] = ["Use ALTERAR_LIMITE, NOVA_CHAVE, TROCAR_SENHA ou RECADASTRO."] });
        }

        if (erros.Count > 0)
        {
            return Validacao.Problema(erros);
        }

        var desafio = await stepUp.AbrirAsync(usuarioId, finalidade, canonico, http.Connection.RemoteIpAddress, cancelamento);
        return Results.Json(new DesafioDeAcaoResponse(desafio.Id, Codigo.De(finalidade), desafio.ExpiraEm, instrucao), statusCode: StatusCodes.Status201Created);
    }

    internal static T? Ler<T>(string? texto, JsonSerializerOptions opcoes) where T : class
    {
        try
        {
            return string.IsNullOrWhiteSpace(texto) ? null : JsonSerializer.Deserialize<T>(texto, opcoes);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Lê a parte multipart de uma ação sensível. Nulo quando não há desafio (a rota então responde STEP_UP_REQUIRED ou JSON puro).</summary>
    internal static async Task<(Confirmacao? Confirmacao, Dictionary<string, string[]> Erros)> LerConfirmacaoAsync(HttpRequest requisicao, CancellationToken cancelamento)
    {
        var erros = new Dictionary<string, string[]>();
        if (!requisicao.HasFormContentType)
        {
            return (null, erros);
        }

        var formulario = await requisicao.ReadFormAsync(cancelamento);
        if (!Guid.TryParse(formulario["desafio_id"], out var desafioId))
        {
            return (null, erros);
        }

        string? senha = formulario["senha"];
        var temImagem = formulario.Files.GetFile("image") is not null;
        if (temImagem == !string.IsNullOrEmpty(senha))
        {
            erros["credencial"] = ["Envie a foto (image) ou a senha, só um dos dois."];
        }
        else if (senha is { Length: > Validacao.SenhaMaxima })
        {
            erros["senha"] = ["Senha longa demais."];
        }

        return (new Confirmacao(formulario["dados"], desafioId, senha, temImagem ? Imagens.LerImagem(formulario) : null), erros);
    }
}

/// <summary>Validação e forma canônica dos parâmetros das ações sensíveis: a mesma função serve ao desafio e à ação, então o hash coincide.</summary>
internal static class AcoesSensiveis
{
    /// <summary>Forma canônica das ações que não levam parâmetros: o desafio fica amarrado só ao usuário e à finalidade.</summary>
    public const string SemParametros = "{}";

    public static Dictionary<string, string[]> ErrosDeLimites(LimitesRequest? corpo, out TimeOnly? inicio)
    {
        var erros = new Dictionary<string, string[]>();
        inicio = null;
        if (corpo is null || (corpo.LimiteDiurnoCentavos is null && corpo.LimiteNoturnoCentavos is null && corpo.InicioNoturno is null))
        {
            erros["corpo"] = ["Envie ao menos um dos limites."];
            return erros;
        }

        if (corpo.LimiteDiurnoCentavos is < 0 || corpo.LimiteNoturnoCentavos is < 0)
        {
            erros["limite"] = ["Os limites não podem ser negativos."];
        }

        if (corpo.InicioNoturno is not null)
        {
            inicio = corpo.InicioNoturno switch
            {
                "20:00" => new TimeOnly(20, 0),
                "22:00" => new TimeOnly(22, 0),
                _ => null,
            };
            if (inicio is null)
            {
                erros["inicio_noturno"] = ["Use 20:00 ou 22:00."];
            }
        }

        return erros;
    }

    public static Dictionary<string, string[]> ErrosDeChave(NovaChaveRequest? corpo, out TipoChave tipo)
    {
        var erros = new Dictionary<string, string[]>();
        tipo = corpo?.Tipo switch
        {
            "EMAIL" => TipoChave.Email,
            "CELULAR" => TipoChave.Celular,
            "ALEATORIA" => TipoChave.Aleatoria,
            _ => TipoChave.Cpf,
        };
        if (tipo == TipoChave.Cpf)
        {
            erros["tipo"] = ["Use EMAIL, CELULAR ou ALEATORIA."];
        }
        else if (tipo == TipoChave.Aleatoria ? corpo!.Valor is not null : string.IsNullOrEmpty(corpo!.Valor) || corpo.Valor.Length > 254)
        {
            erros["valor"] = [tipo == TipoChave.Aleatoria ? "A chave aleatória é gerada pelo servidor: não envie valor." : "Informe o valor da chave."];
        }

        return erros;
    }

    /// <summary>JSON com as chaves em ordem alfabética e só o que foi informado: é o que entra no hash do desafio.</summary>
    public static string Canonico(LimitesRequest? l)
    {
        var campos = new SortedDictionary<string, object>(StringComparer.Ordinal);
        if (l?.InicioNoturno is not null)
        {
            campos["inicio_noturno"] = l.InicioNoturno;
        }

        if (l?.LimiteDiurnoCentavos is not null)
        {
            campos["limite_diurno_centavos"] = l.LimiteDiurnoCentavos;
        }

        if (l?.LimiteNoturnoCentavos is not null)
        {
            campos["limite_noturno_centavos"] = l.LimiteNoturnoCentavos;
        }

        return JsonSerializer.Serialize(campos);
    }

    public static string Canonico(NovaChaveRequest? c)
    {
        var campos = new SortedDictionary<string, object>(StringComparer.Ordinal);
        if (c?.Tipo is not null)
        {
            campos["tipo"] = c.Tipo;
        }

        if (c?.Valor is not null)
        {
            campos["valor"] = c.Valor;
        }

        return JsonSerializer.Serialize(campos);
    }
}
