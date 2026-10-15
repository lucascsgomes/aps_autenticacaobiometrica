using System.Security.Claims;
using System.Text.Json;
using Ministerio.Api.Seguranca;
using Ministerio.Contratos;
using Ministerio.Dominio;
using Ministerio.Dominio.Acesso;
using Ministerio.Dominio.Biometria;
using Ministerio.Infra.Acesso;
using Ministerio.Infra.Autenticacao;
using Ministerio.Infra.Persistencia;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Ministerio.Api.Endpoints;

/// <summary>O que vem junto de uma ação sensível: os dados dela, o desafio e a credencial (rosto ou senha).</summary>
internal sealed record Confirmacao(string? Dados, Guid DesafioId, string? Senha, byte[]? Imagem);

internal static class StepUpEndpoints
{
    public static IEndpointRouteBuilder MapearStepUp(this IEndpointRouteBuilder rotas)
    {
        rotas.MapGroup("/v1/biometria").RequireAuthorization().RequireRateLimiting(Limites.BiometriaAutenticada)
            .MapPost("/desafios", AbrirDesafio);
        return rotas;
    }

    // Abre o desafio de uma ação sensível. Pedir um nível acima do seu já é recusado aqui, antes de gastar o rosto, e fica no relatório.
    private static async Task<IResult> AbrirDesafio(
        DesafioDeAcaoRequest? corpo,
        HttpContext http,
        ClaimsPrincipal usuario,
        ServicoDeStepUp stepUp,
        MinisterioDbContext db,
        ServicoDeNiveis niveis,
        ServicoDeAuditoria auditoria,
        CancellationToken cancelamento)
    {
        if (!Guid.TryParse(usuario.FindFirstValue(JwtRegisteredClaimNames.Sub), out var usuarioId))
        {
            return Results.Unauthorized();
        }

        FinalidadeDoDesafio finalidade;
        string instrucao;
        switch (corpo?.Finalidade)
        {
            case "ACESSO_NIVEL_2":
                (finalidade, instrucao) = (FinalidadeDoDesafio.AcessoNivel2, "Mostre o rosto para abrir as informações de nível 2.");
                break;
            case "ACESSO_NIVEL_3":
                (finalidade, instrucao) = (FinalidadeDoDesafio.AcessoNivel3, "Mostre o rosto para abrir as informações de nível 3.");
                break;
            case "TROCAR_SENHA":
                (finalidade, instrucao) = (FinalidadeDoDesafio.TrocarSenha, "Confirme com o rosto ou com a senha atual a troca de senha.");
                break;
            case "RECADASTRO":
                (finalidade, instrucao) = (FinalidadeDoDesafio.Recadastro, "Confirme com o rosto atual ou com a senha a troca do rosto.");
                break;
            default:
                return Validacao.Problema(new Dictionary<string, string[]> { ["finalidade"] = ["Use ACESSO_NIVEL_2, ACESSO_NIVEL_3, TROCAR_SENHA ou RECADASTRO."] });
        }

        if (finalidade is FinalidadeDoDesafio.AcessoNivel2 or FinalidadeDoDesafio.AcessoNivel3)
        {
            var exigido = finalidade == FinalidadeDoDesafio.AcessoNivel3 ? Niveis.Ministro : Niveis.Diretor;
            if (await db.ObterAsync(niveis, usuarioId, cancelamento) < exigido)
            {
                await auditoria.RegistrarAsync(usuarioId, EventoDaConsulta(exigido), ResultadoDoAcesso.Recusado, "NIVEL_INSUFICIENTE", http.Connection.RemoteIpAddress);
                throw new NivelInsuficienteException(exigido);
            }
        }

        var desafio = await stepUp.AbrirAsync(usuarioId, finalidade, AcoesSensiveis.SemParametros, http.Connection.RemoteIpAddress, cancelamento);
        return Results.Json(new DesafioDeAcaoResponse(desafio.Id, Codigo.De(finalidade), desafio.ExpiraEm, instrucao), statusCode: StatusCodes.Status201Created);
    }

    internal static EventoDeAcesso EventoDaConsulta(int nivel) => nivel switch
    {
        Niveis.Ministro => EventoDeAcesso.ConsultaNivel3,
        Niveis.Diretor => EventoDeAcesso.ConsultaNivel2,
        _ => EventoDeAcesso.ConsultaNivel1,
    };

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

    /// <summary>Lê a parte multipart de uma ação sensível. Nulo quando não há desafio (a rota então responde STEP_UP_REQUIRED).</summary>
    internal static async Task<(Confirmacao? Confirmacao, Dictionary<string, string[]> Erros)> LerConfirmacaoAsync(HttpRequest requisicao, CancellationToken cancelamento)
    {
        var erros = new Dictionary<string, string[]>();
        if (!requisicao.HasFormContentType)
        {
            return (null, erros);
        }

        var formulario = await Imagens.LerFormularioAsync(requisicao, cancelamento);
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

/// <summary>As ações sensíveis daqui não levam parâmetros: o desafio fica amarrado só ao usuário e à finalidade.</summary>
internal static class AcoesSensiveis
{
    public const string SemParametros = "{}";
}
