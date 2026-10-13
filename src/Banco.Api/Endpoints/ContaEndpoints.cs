using System.Globalization;
using System.Security.Claims;
using System.Text;
using Banco.Api.Seguranca;
using Banco.Contratos;
using Banco.Dominio;
using Banco.Dominio.Chaves;
using Banco.Dominio.Transferencias;
using Banco.Infra.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Banco.Api.Endpoints;

internal static class ContaEndpoints
{
    private const int LimitePadrao = 20;
    private const int LimiteMaximo = 100;
    private const string Instituicao = "Banco APS";

    public static IEndpointRouteBuilder MapearContas(this IEndpointRouteBuilder rotas)
    {
        var contas = rotas.MapGroup("/v1/contas").RequireAuthorization().RequireRateLimiting(Limites.Leitura);
        contas.MapGet("/me", ContaDoUsuario);
        contas.MapGet("/me/extrato", Extrato);
        contas.MapDelete("/me/chaves/{id:guid}", ApagarChave);

        rotas.MapGet("/v1/chaves/{valor}", ConsultarChave).RequireAuthorization().RequireRateLimiting(Limites.ConsultaChave);
        return rotas;
    }

    // A conta sai sempre do usuário do token, nunca de um id vindo na URL: usuário A não tem como pedir a conta de B.
    private static Guid? UsuarioDoToken(ClaimsPrincipal usuario) =>
        Guid.TryParse(usuario.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id) ? id : null;

    private static async Task<IResult> ContaDoUsuario(ClaimsPrincipal usuario, BancoDbContext banco, CancellationToken cancelamento)
    {
        if (UsuarioDoToken(usuario) is not { } usuarioId)
        {
            return Results.Unauthorized();
        }

        var conta = await banco.Contas
            .Where(c => c.UsuarioId == usuarioId)
            .Join(banco.Usuarios, c => c.UsuarioId, u => u.Id, (c, u) => new { Conta = c, u.Nome })
            .SingleOrDefaultAsync(cancelamento);
        if (conta is null)
        {
            return Results.NotFound();
        }

        var chaves = (await banco.Chaves.Where(k => k.ContaId == conta.Conta.Id).OrderBy(k => k.CriadaEm).ToListAsync(cancelamento))
            .Select(k => new ChaveDto(k.Id, Codigo(k.Tipo), Mascara.Chave(k.Tipo, k.Valor)))
            .ToList();

        return Results.Ok(new ContaMeResponse(
            conta.Conta.Id,
            conta.Conta.Agencia,
            conta.Conta.Numero,
            conta.Conta.Status.ToString().ToUpperInvariant(),
            conta.Conta.SaldoCentavos,
            new TitularDto(conta.Nome),
            chaves));
    }

    private static async Task<IResult> Extrato(
        ClaimsPrincipal usuario,
        BancoDbContext banco,
        CancellationToken cancelamento,
        int? limit = null,
        string? cursor = null,
        string? de = null,
        string? ate = null,
        string? tipo = null)
    {
        if (UsuarioDoToken(usuario) is not { } usuarioId)
        {
            return Results.Unauthorized();
        }

        var erros = new Dictionary<string, string[]>();
        var tamanho = limit ?? LimitePadrao;
        if (tamanho is < 1 or > LimiteMaximo)
        {
            erros["limit"] = [$"Use de 1 a {LimiteMaximo}."];
        }

        (DateTimeOffset Em, long Id)? posicao = null;
        if (cursor is not null)
        {
            posicao = LerCursor(cursor);
            if (posicao is null)
            {
                erros["cursor"] = ["Cursor inválido."];
            }
        }

        var desde = LerData(de, "de", erros);
        var ateInclusivo = LerData(ate, "ate", erros);
        if (tipo is not (null or "D" or "C"))
        {
            erros["tipo"] = ["Use D (saídas) ou C (entradas)."];
        }

        if (erros.Count > 0)
        {
            return Validacao.Problema(erros);
        }

        var contaId = await banco.Contas.Where(c => c.UsuarioId == usuarioId).Select(c => c.Id).SingleOrDefaultAsync(cancelamento);
        if (contaId == Guid.Empty)
        {
            return Results.NotFound();
        }

        var consulta = banco.Lancamentos.Where(l => l.ContaId == contaId);
        if (tipo is not null)
        {
            var t = tipo == "D" ? TipoLancamento.Debito : TipoLancamento.Credito;
            consulta = consulta.Where(l => l.Tipo == t);
        }

        if (desde is { } inicio)
        {
            var d = ComecoDoDia(inicio);
            consulta = consulta.Where(l => l.CriadoEm >= d);
        }

        if (ateInclusivo is { } fim)
        {
            var a = ComecoDoDia(fim.AddDays(1));
            consulta = consulta.Where(l => l.CriadoEm < a);
        }

        if (posicao is var (em, id))
        {
            consulta = consulta.Where(l => l.CriadoEm < em || (l.CriadoEm == em && l.Id < id));
        }

        // Pede um a mais: se ele existir, há próxima página. A contraparte é a outra perna da mesma transferência.
        var linhas = await consulta
            .OrderByDescending(l => l.CriadoEm).ThenByDescending(l => l.Id)
            .Take(tamanho + 1)
            .Join(banco.Transferencias, l => l.TransferenciaId, t => t.Id, (l, t) => new { L = l, T = t })
            .Join(banco.Lancamentos, x => x.L.TransferenciaId, o => o.TransferenciaId, (x, o) => new { x.L, x.T, Outra = o })
            .Where(x => x.Outra.ContaId != contaId)
            .Join(banco.Contas, x => x.Outra.ContaId, c => c.Id, (x, c) => new { x.L, x.T, Conta = c })
            .GroupJoin(banco.Usuarios, x => x.Conta.UsuarioId, u => u.Id, (x, us) => new { x.L, x.T, x.Conta, Us = us })
            .SelectMany(x => x.Us.DefaultIfEmpty(), (x, u) => new { x.L, x.T, x.Conta, Nome = u == null ? null : u.Nome })
            .OrderByDescending(x => x.L.CriadoEm).ThenByDescending(x => x.L.Id)
            .ToListAsync(cancelamento);

        var haMais = linhas.Count > tamanho;
        var pagina = linhas.Take(tamanho).ToList();
        var itens = pagina.Select(x => new ExtratoItemDto(
            x.L.Id,
            x.L.TransferenciaId,
            x.L.Tipo == TipoLancamento.Debito ? "D" : "C",
            x.L.ValorCentavos,
            x.L.SaldoAposCentavos,
            new ContraparteDto(x.Nome is null ? Instituicao : Mascara.Nome(x.Nome), Mascara.Conta(x.Conta.Numero)),
            x.T.Descricao,
            x.T.Status.ToString().ToUpperInvariant(),
            x.L.CriadoEm)).ToList();

        var proximo = haMais ? EscreverCursor(pagina[^1].L.CriadoEm, pagina[^1].L.Id) : null;
        return Results.Ok(new ExtratoResponse(itens, proximo, haMais));
    }

    private static async Task<IResult> ConsultarChave(string valor, ClaimsPrincipal usuario, BancoDbContext banco, CancellationToken cancelamento)
    {
        if (UsuarioDoToken(usuario) is not { } usuarioId)
        {
            return Results.Unauthorized();
        }

        var tipo = ChaveTransferencia.Classificar(valor) ?? throw new ChaveInvalidaException();
        var achada = await banco.Chaves
            .Where(k => k.Valor == valor)
            .Join(banco.Contas, k => k.ContaId, c => c.Id, (k, c) => c.UsuarioId)
            .Join(banco.Usuarios, uid => uid, u => u.Id, (uid, u) => new { u.Id, u.Nome })
            .SingleOrDefaultAsync(cancelamento) ?? throw new ChaveNaoEncontradaException();

        return Results.Ok(new ConsultaChaveResponse(Codigo(tipo), Mascara.Chave(tipo, valor), Mascara.Nome(achada.Nome), Instituicao, achada.Id == usuarioId));
    }

    private static async Task<IResult> ApagarChave(Guid id, ClaimsPrincipal usuario, BancoDbContext banco, CancellationToken cancelamento)
    {
        if (UsuarioDoToken(usuario) is not { } usuarioId)
        {
            return Results.Unauthorized();
        }

        // Filtra pela conta do token: apagar chave alheia responde igual a chave inexistente.
        var apagadas = await banco.Chaves
            .Where(k => k.Id == id && banco.Contas.Any(c => c.Id == k.ContaId && c.UsuarioId == usuarioId))
            .ExecuteDeleteAsync(cancelamento);
        return apagadas == 0 ? throw new ChaveNaoEncontradaException() : Results.NoContent();
    }

    private static string Codigo(TipoChave tipo) => tipo.ToString().ToUpperInvariant();

    private static DateOnly? LerData(string? texto, string campo, Dictionary<string, string[]> erros)
    {
        if (texto is null)
        {
            return null;
        }

        if (DateOnly.TryParseExact(texto, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var data))
        {
            return data;
        }

        erros[campo] = ["Use o formato yyyy-MM-dd."];
        return null;
    }

    private static DateTimeOffset ComecoDoDia(DateOnly dia)
    {
        var fuso = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        var local = dia.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, fuso.GetUtcOffset(local)).ToUniversalTime(); // Npgsql só grava timestamptz com offset 0
    }

    private static string EscreverCursor(DateTimeOffset em, long id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{em.UtcTicks}|{id}")).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static (DateTimeOffset, long)? LerCursor(string cursor)
    {
        try
        {
            var b64 = cursor.Replace('-', '+').Replace('_', '/');
            b64 = b64.PadRight(b64.Length + ((4 - (b64.Length % 4)) % 4), '=');
            var partes = Encoding.UTF8.GetString(Convert.FromBase64String(b64)).Split('|');
            return partes.Length == 2
                && long.TryParse(partes[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                && long.TryParse(partes[1], NumberStyles.None, CultureInfo.InvariantCulture, out var id)
                && ticks <= DateTimeOffset.MaxValue.Ticks
                ? (new DateTimeOffset(ticks, TimeSpan.Zero), id)
                : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
