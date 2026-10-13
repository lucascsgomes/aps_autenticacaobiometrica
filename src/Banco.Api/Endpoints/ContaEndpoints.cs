using System.Security.Claims;
using Banco.Contratos;
using Banco.Infra.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Banco.Api.Endpoints;

internal static class ContaEndpoints
{
    public static IEndpointRouteBuilder MapearContas(this IEndpointRouteBuilder rotas)
    {
        var contas = rotas.MapGroup("/v1/contas").RequireAuthorization();
        contas.MapGet("/me", ContaDoUsuario);
        return rotas;
    }

    // A conta sai sempre do usuário do token, nunca de um id vindo na URL: usuário A não tem como pedir a conta de B.
    private static async Task<IResult> ContaDoUsuario(ClaimsPrincipal usuario, BancoDbContext banco, CancellationToken cancelamento)
    {
        if (!Guid.TryParse(usuario.FindFirstValue(JwtRegisteredClaimNames.Sub), out var usuarioId))
        {
            return Results.Unauthorized();
        }

        var conta = await banco.Contas
            .Where(c => c.UsuarioId == usuarioId)
            .Join(banco.Usuarios, c => c.UsuarioId, u => u.Id, (c, u) => new { Conta = c, u.Nome })
            .SingleOrDefaultAsync(cancelamento);

        return conta is null
            ? Results.NotFound()
            : Results.Ok(new ContaMeResponse(
                conta.Conta.Id,
                conta.Conta.Agencia,
                conta.Conta.Numero,
                conta.Conta.Status.ToString().ToUpperInvariant(),
                conta.Conta.SaldoCentavos,
                new TitularDto(conta.Nome)));
    }
}
