using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using Ministerio.Api.Seguranca;
using Ministerio.Contratos;
using Ministerio.Dominio;
using Ministerio.Dominio.Acesso;
using Ministerio.Dominio.Biometria;
using Ministerio.Dominio.Usuarios;
using Ministerio.Infra.Acesso;
using Ministerio.Infra.Autenticacao;
using Ministerio.Infra.Persistencia;
using RequestSizeLimitAttribute = Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute;

namespace Ministerio.Api.Endpoints;

/// <summary>
/// Painel de acesso: o admin vê os cadastrados e muda o nível (1 a 3) e o perfil de admin. Cada mudança exige o rosto do admin,
/// amarrado a este usuário e a estes valores, e fica no relatório. A senha não vale aqui.
/// </summary>
internal static class AdminEndpoints
{
    private const int TamanhoMaximoDoDetalhe = 200;

    public static IEndpointRouteBuilder MapearAdmin(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/v1/admin").RequireAuthorization();
        grupo.MapGet("/usuarios", Listar).RequireRateLimiting(Limites.Leitura);
        grupo.MapPut("/usuarios/{id:guid}/acesso", Alterar)
            .RequireRateLimiting(Limites.BiometriaAutenticada)
            .WithMetadata(new RequestSizeLimitAttribute(Imagens.TamanhoMaximoDaRequisicao));
        return rotas;
    }

    private static Guid? UsuarioDoToken(ClaimsPrincipal usuario) =>
        Guid.TryParse(usuario.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id) ? id : null;

    private static async Task<IResult> Listar(ClaimsPrincipal usuario, MinisterioDbContext db, ServicoDeAdmins admins, CancellationToken cancelamento)
    {
        if (UsuarioDoToken(usuario) is not { } usuarioId)
        {
            return Results.Unauthorized();
        }

        if (!(await db.ObterAsync(admins, usuarioId, cancelamento)).Admin)
        {
            throw new NaoEhAdminException();
        }

        var linhas = await db.Usuarios.AsNoTracking()
            .OrderBy(u => u.Nome)
            .Select(u => new { u.Id, u.Nome, u.Cpf, u.Nivel, u.Admin, u.Status, u.CriadoEm, TemRosto = db.Templates.Any(t => t.UsuarioId == u.Id) })
            .ToListAsync(cancelamento);
        return Results.Ok(linhas.Select(u => new UsuarioAdminDto(
            u.Id,
            u.Nome,
            Cpf.Mascarar(u.Cpf),
            u.Nivel,
            Niveis.Cargo(u.Nivel),
            u.TemRosto,
            admins.EhAdmin(u.Cpf, u.Admin),
            admins.Fixo(u.Cpf),
            Codigo.De(u.Status),
            u.CriadoEm,
            u.Id == usuarioId)).ToList());
    }

    // multipart: dados (JSON com nivel e/ou admin), desafio_id e image. O desafio vem de /v1/biometria/desafios (ALTERAR_ACESSO).
    private static async Task<IResult> Alterar(
        Guid id,
        HttpContext http,
        ClaimsPrincipal usuario,
        MinisterioDbContext db,
        ServicoDeAdmins admins,
        ServicoDeStepUp stepUp,
        ServicoDeAuditoria auditoria,
        IOptions<JsonOptions> json,
        TimeProvider relogio,
        CancellationToken cancelamento)
    {
        if (UsuarioDoToken(usuario) is not { } usuarioId)
        {
            return Results.Unauthorized();
        }

        var ip = http.Connection.RemoteIpAddress;
        try
        {
            if (!(await db.ObterAsync(admins, usuarioId, cancelamento)).Admin)
            {
                throw new NaoEhAdminException();
            }

            var (confirmacao, erros) = await StepUpEndpoints.LerConfirmacaoAsync(http.Request, cancelamento);
            if (confirmacao is null)
            {
                throw new ConfirmacaoNecessariaException(Codigo.De(FinalidadeDoDesafio.AlterarAcesso));
            }

            var pedido = (StepUpEndpoints.Ler<AlterarAcessoRequest>(confirmacao.Dados, json.Value.SerializerOptions) ?? new AlterarAcessoRequest(null, null, null)) with { UsuarioId = id };
            foreach (var (campo, mensagens) in AcoesSensiveis.ErrosDoAcesso(pedido))
            {
                erros[campo] = mensagens;
            }

            if (erros.Count > 0)
            {
                return Validacao.Problema(erros);
            }

            if (confirmacao.Imagem is null)
            {
                throw new ExigeRostoException();
            }

            var alvo = await db.Usuarios.SingleOrDefaultAsync(u => u.Id == id && u.Status == StatusUsuario.Ativo, cancelamento) ?? throw new UsuarioNaoEncontradoException();
            if (pedido.Admin == false && admins.Fixo(alvo.Cpf))
            {
                throw new MudancaNaoPermitidaException("Este admin vem da configuração do servidor, e o painel não consegue tirá-lo.");
            }

            if (pedido.Admin == false && id == usuarioId)
            {
                throw new MudancaNaoPermitidaException("Você não pode tirar o seu próprio perfil de admin.");
            }

            await stepUp.ConfirmarAsync(usuarioId, FinalidadeDoDesafio.AlterarAcesso, AcoesSensiveis.Canonico(id, pedido.Nivel, pedido.Admin), confirmacao.DesafioId, null, confirmacao.Imagem, http.TraceIdentifier, cancelamento);

            var antes = $"nível {alvo.Nivel}, admin {(alvo.Admin ? "sim" : "não")}";
            alvo.DefinirAcesso(pedido.Nivel, pedido.Admin, relogio.GetUtcNow());
            await db.SaveChangesAsync(cancelamento);

            var detalhe = $"{alvo.Nome}: de {antes} para nível {alvo.Nivel}, admin {(alvo.Admin ? "sim" : "não")}";
            await auditoria.RegistrarAsync(usuarioId, EventoDeAcesso.AlteracaoDeAcesso, ResultadoDoAcesso.Aceito, null, ip, detalhe.Length > TamanhoMaximoDoDetalhe ? detalhe[..TamanhoMaximoDoDetalhe] : detalhe);
            return Results.NoContent();
        }
        catch (NegocioException e)
        {
            await auditoria.RegistrarAsync(usuarioId, EventoDeAcesso.AlteracaoDeAcesso, ResultadoDoAcesso.Recusado, e.Code, ip);
            throw;
        }
    }
}
