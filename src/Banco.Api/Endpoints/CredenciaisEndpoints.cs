using System.Security.Claims;
using System.Text.Json;
using Banco.Api.Seguranca;
using Banco.Contratos;
using Banco.Dominio;
using Banco.Dominio.Biometria;
using Banco.Infra.Autenticacao;
using Banco.Infra.Facial;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using RequestSizeLimitAttribute = Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute;

namespace Banco.Api.Endpoints;

/// <summary>Rosto cadastrado (consultar, trocar, excluir) e troca de senha. Trocar o rosto e a senha passam pelo step-up e encerram as sessões.</summary>
internal static class CredenciaisEndpoints
{
    // Trocar o rosto leva duas fotos: a que prova quem é (image) e a nova (nova_image).
    private const int LimiteDuasFotos = (2 * Imagens.TamanhoMaximoDaImagem) + (128 * 1024);

    public static IEndpointRouteBuilder MapearCredenciais(this IEndpointRouteBuilder rotas)
    {
        var me = rotas.MapGroup("/v1/me").RequireAuthorization();
        me.MapGet("/biometria", Biometria).RequireRateLimiting(Limites.Leitura);
        me.MapPut("/biometria", TrocarRosto).RequireRateLimiting(Limites.Biometria).WithMetadata(new RequestSizeLimitAttribute(LimiteDuasFotos));
        me.MapDelete("/biometria", ExcluirRosto).RequireRateLimiting(Limites.Biometria);
        me.MapPut("/senha", TrocarSenha).RequireRateLimiting(Limites.Biometria).WithMetadata(new RequestSizeLimitAttribute(Imagens.TamanhoMaximoDaRequisicao));
        return rotas;
    }

    private static Guid? UsuarioDoToken(ClaimsPrincipal usuario) =>
        Guid.TryParse(usuario.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id) ? id : null;

    private static async Task<IResult> Biometria(ClaimsPrincipal usuario, ServicoDeCredenciais credenciais, CancellationToken cancelamento) =>
        UsuarioDoToken(usuario) is { } id ? Results.Ok(Montar(await credenciais.RostoAsync(id, cancelamento))) : Results.Unauthorized();

    private static BiometriaResponse Montar(TemplateFacial? rosto) => new(rosto is not null, rosto?.CriadoEm, rosto?.Modelo);

    // A foto nova é lida antes de gastar o desafio: sem rosto ou com vários o usuário tenta de novo com o mesmo desafio.
    private static async Task<IResult> TrocarRosto(
        HttpContext http,
        ClaimsPrincipal usuario,
        ServicoDeStepUp stepUp,
        ServicoDeCredenciais credenciais,
        IClienteFacial facial,
        CancellationToken cancelamento)
    {
        if (UsuarioDoToken(usuario) is not { } id)
        {
            return Results.Unauthorized();
        }

        var (confirmacao, erros) = await StepUpEndpoints.LerConfirmacaoAsync(http.Request, cancelamento);
        if (confirmacao is null)
        {
            throw new ConfirmacaoNecessariaException("RECADASTRO");
        }

        if (erros.Count > 0)
        {
            return Validacao.Problema(erros);
        }

        var nova = Imagens.LerImagem(http.Request.Form, "nova_image");
        var embedding = await facial.CadastrarAsync(nova, http.TraceIdentifier, cancelamento);
        await stepUp.ConfirmarAsync(id, FinalidadeDoDesafio.Recadastro, AcoesSensiveis.SemParametros, confirmacao.DesafioId, confirmacao.Senha, confirmacao.Imagem, http.TraceIdentifier, cancelamento);
        return Results.Ok(Montar(await credenciais.SubstituirRostoAsync(id, embedding, cancelamento)));
    }

    // Exige a senha e nada mais: quem perdeu o rosto (ou não quer mais) precisa conseguir apagar sem depender dele.
    private static async Task<IResult> ExcluirRosto(
        HttpContext http,
        ClaimsPrincipal usuario,
        ServicoDeStepUp stepUp,
        ServicoDeCredenciais credenciais,
        IOptions<JsonOptions> json,
        CancellationToken cancelamento)
    {
        if (UsuarioDoToken(usuario) is not { } id)
        {
            return Results.Unauthorized();
        }

        var corpo = http.Request.HasJsonContentType() ? await LerJsonAsync<ConfirmarSenhaRequest>(http.Request, json.Value.SerializerOptions, cancelamento) : null;
        if (string.IsNullOrEmpty(corpo?.Senha) || corpo.Senha.Length > Validacao.SenhaMaxima)
        {
            return Validacao.Problema(new Dictionary<string, string[]> { ["senha"] = ["Informe a senha."] });
        }

        await stepUp.ConfirmarSenhaAsync(id, corpo.Senha, cancelamento);
        await credenciais.ExcluirRostoAsync(id, cancelamento);
        return Results.NoContent();
    }

    // multipart: dados {"nova_senha"}, desafio_id e a credencial (image, ou senha = a senha atual).
    private static async Task<IResult> TrocarSenha(
        HttpContext http,
        ClaimsPrincipal usuario,
        ServicoDeStepUp stepUp,
        ServicoDeCredenciais credenciais,
        IOptions<JsonOptions> json,
        CancellationToken cancelamento)
    {
        if (UsuarioDoToken(usuario) is not { } id)
        {
            return Results.Unauthorized();
        }

        var (confirmacao, erros) = await StepUpEndpoints.LerConfirmacaoAsync(http.Request, cancelamento);
        if (confirmacao is null)
        {
            throw new ConfirmacaoNecessariaException("TROCAR_SENHA");
        }

        var corpo = StepUpEndpoints.Ler<TrocarSenhaRequest>(confirmacao.Dados, json.Value.SerializerOptions);
        foreach (var (campo, mensagens) in Validacao.NovaSenha(corpo?.NovaSenha))
        {
            erros[campo] = mensagens;
        }

        if (erros.Count > 0)
        {
            return Validacao.Problema(erros);
        }

        await stepUp.ConfirmarAsync(id, FinalidadeDoDesafio.TrocarSenha, AcoesSensiveis.SemParametros, confirmacao.DesafioId, confirmacao.Senha, confirmacao.Imagem, http.TraceIdentifier, cancelamento);
        await credenciais.TrocarSenhaAsync(id, corpo!.NovaSenha!, cancelamento);
        return Results.NoContent();
    }

    private static async Task<T?> LerJsonAsync<T>(HttpRequest requisicao, JsonSerializerOptions opcoes, CancellationToken cancelamento) where T : class
    {
        try
        {
            return await requisicao.ReadFromJsonAsync<T>(opcoes, cancelamento);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
