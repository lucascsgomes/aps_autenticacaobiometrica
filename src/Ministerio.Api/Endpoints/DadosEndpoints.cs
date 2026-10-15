using System.Security.Claims;
using Ministerio.Api.Seguranca;
using Ministerio.Contratos;
using Ministerio.Dominio;
using Ministerio.Dominio.Acesso;
using Ministerio.Infra.Acesso;
using Ministerio.Infra.Autenticacao;
using Ministerio.Infra.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using RequestSizeLimitAttribute = Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute;

namespace Ministerio.Api.Endpoints;

/// <summary>
/// A base ambiental em três níveis. Nível 1 abre com a sessão; níveis 2 e 3 pedem, a cada consulta, o rosto confirmado na hora
/// (limiar mais rígido quanto mais alto o nível), e só para quem tem aquele nível ou acima. A senha não abre nenhum nível restrito.
/// </summary>
internal static class DadosEndpoints
{
    public static IEndpointRouteBuilder MapearDados(this IEndpointRouteBuilder rotas)
    {
        var dados = rotas.MapGroup("/v1/dados").RequireAuthorization();
        dados.MapGet("/1", Nivel1).RequireRateLimiting(Limites.Leitura);
        dados.MapPost("/{nivel:int:range(2,3)}", Restrito)
            .RequireRateLimiting(Limites.BiometriaAutenticada)
            .WithMetadata(new RequestSizeLimitAttribute(Imagens.TamanhoMaximoDaRequisicao));
        return rotas;
    }

    private static async Task<IResult> Nivel1(HttpContext http, ClaimsPrincipal usuario, MinisterioDbContext db, ServicoDeAuditoria auditoria, CancellationToken cancelamento)
    {
        if (!Guid.TryParse(usuario.FindFirstValue(JwtRegisteredClaimNames.Sub), out var usuarioId))
        {
            return Results.Unauthorized();
        }

        var resposta = await Montar(db, Niveis.Servidor, cancelamento);
        await auditoria.RegistrarAsync(usuarioId, EventoDeAcesso.ConsultaNivel1, ResultadoDoAcesso.Aceito, null, http.Connection.RemoteIpAddress);
        return Results.Ok(resposta);
    }

    // multipart: desafio_id (aberto em /v1/biometria/desafios) e image. Qualquer recusa, inclusive a de nível, entra no relatório.
    private static async Task<IResult> Restrito(
        int nivel,
        HttpContext http,
        ClaimsPrincipal usuario,
        MinisterioDbContext db,
        ServicoDeAdmins admins,
        ServicoDeStepUp stepUp,
        ServicoDeAuditoria auditoria,
        CancellationToken cancelamento)
    {
        if (!Guid.TryParse(usuario.FindFirstValue(JwtRegisteredClaimNames.Sub), out var usuarioId))
        {
            return Results.Unauthorized();
        }

        var evento = StepUpEndpoints.EventoDaConsulta(nivel);
        var ip = http.Connection.RemoteIpAddress;
        var finalidade = Niveis.FinalidadeDoAcesso(nivel)!.Value;
        try
        {
            if ((await db.ObterAsync(admins, usuarioId, cancelamento)).Nivel < nivel)
            {
                throw new NivelInsuficienteException(nivel);
            }

            var (confirmacao, erros) = await StepUpEndpoints.LerConfirmacaoAsync(http.Request, cancelamento);
            if (confirmacao is null)
            {
                throw new ConfirmacaoNecessariaException(Codigo.De(finalidade));
            }

            if (erros.Count > 0)
            {
                return Validacao.Problema(erros);
            }

            if (confirmacao.Imagem is null)
            {
                throw new ExigeRostoException();
            }

            await stepUp.ConfirmarAsync(usuarioId, finalidade, AcoesSensiveis.SemParametros, confirmacao.DesafioId, null, confirmacao.Imagem, http.TraceIdentifier, cancelamento);
        }
        catch (NegocioException e)
        {
            await auditoria.RegistrarAsync(usuarioId, evento, ResultadoDoAcesso.Recusado, e.Code, ip);
            throw;
        }

        var resposta = await Montar(db, nivel, cancelamento);
        await auditoria.RegistrarAsync(usuarioId, evento, ResultadoDoAcesso.Aceito, null, ip);
        return Results.Ok(resposta);
    }

    private static async Task<DadosResponse> Montar(MinisterioDbContext db, int nivel, CancellationToken cancelamento)
    {
        var linhas = await db.Propriedades.AsNoTracking().OrderBy(p => p.Uf).ThenBy(p => p.Municipio).ThenBy(p => p.Ano).ToListAsync(cancelamento);
        return new DadosResponse(nivel, linhas.Select(p => Projetar(p, nivel)).ToList());
    }

    /// <summary>Só sai do servidor o que o nível permite: o resto nem é lido para a resposta.</summary>
    internal static PropriedadeDto Projetar(PropriedadeRural p, int nivel) => new(
        p.Id,
        p.Municipio,
        p.Uf,
        p.Agrotoxico,
        p.Ano,
        nivel >= Niveis.Diretor ? p.Propriedade : null,
        nivel >= Niveis.Diretor ? p.QuantidadeLitros : null,
        nivel >= Niveis.Diretor ? p.AreaHectares : null,
        nivel >= Niveis.Ministro ? p.Proprietario : null,
        nivel >= Niveis.Ministro ? p.Documento : null,
        nivel >= Niveis.Ministro ? p.MultaCentavos : null,
        nivel >= Niveis.Ministro ? p.Situacao : null);
}
