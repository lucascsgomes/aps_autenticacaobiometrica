using System.Globalization;
using System.Security.Claims;
using System.Text;
using Ministerio.Api.Seguranca;
using Ministerio.Contratos;
using Ministerio.Dominio;
using Ministerio.Dominio.Acesso;
using Ministerio.Infra.Acesso;
using Ministerio.Infra.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Ministerio.Api.Endpoints;

/// <summary>Relatório de segurança: quem tentou entrar e abrir o quê, e o que foi recusado. Só o ministro (nível 3) vê.</summary>
internal static class RelatorioEndpoints
{
    private const int Recentes = 200;
    private const int LimiteDoCsv = 5000;

    public static IEndpointRouteBuilder MapearRelatorios(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/v1/relatorios").RequireAuthorization().RequireRateLimiting(Limites.Leitura);
        grupo.MapGet("/resumo", Resumo);
        grupo.MapGet("/acessos.csv", Csv);
        return rotas;
    }

    private static async Task<Guid> ExigirMinistroAsync(HttpContext http, ClaimsPrincipal usuario, MinisterioDbContext db, ServicoDeAdmins admins, ServicoDeAuditoria auditoria, CancellationToken cancelamento)
    {
        var usuarioId = Guid.TryParse(usuario.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id) ? id : throw new NaoAutenticadoException();
        if ((await db.ObterAsync(admins, usuarioId, cancelamento)).Nivel < Niveis.Ministro)
        {
            await auditoria.RegistrarAsync(usuarioId, EventoDeAcesso.Relatorio, ResultadoDoAcesso.Recusado, "NIVEL_INSUFICIENTE", http.Connection.RemoteIpAddress);
            throw new NivelInsuficienteException(Niveis.Ministro);
        }

        return usuarioId;
    }

    private static async Task<IResult> Resumo(HttpContext http, ClaimsPrincipal usuario, MinisterioDbContext db, ServicoDeAdmins admins, ServicoDeAuditoria auditoria, CancellationToken cancelamento)
    {
        var usuarioId = await ExigirMinistroAsync(http, usuario, db, admins, auditoria, cancelamento);

        var acessos = db.Acessos.AsNoTracking();
        var total = await acessos.CountAsync(cancelamento);
        var aceitos = await acessos.CountAsync(a => a.Resultado == ResultadoDoAcesso.Aceito, cancelamento);
        var indevidas = await acessos.CountAsync(a => a.Motivo == "NIVEL_INSUFICIENTE", cancelamento);
        var porEvento = (await acessos.GroupBy(a => a.Evento).Select(g => new { g.Key, Total = g.Count() }).ToListAsync(cancelamento))
            .Select(g => new ContagemDto(Codigo.De(g.Key), g.Total)).OrderByDescending(c => c.Total).ToList();
        var porMotivo = (await acessos.Where(a => a.Motivo != null).GroupBy(a => a.Motivo!).Select(g => new { g.Key, Total = g.Count() }).ToListAsync(cancelamento))
            .Select(g => new ContagemDto(g.Key, g.Total)).OrderByDescending(c => c.Total).ToList();
        var recentes = (await Linhas(db, Recentes, cancelamento)).Select(Para).ToList();

        await auditoria.RegistrarAsync(usuarioId, EventoDeAcesso.Relatorio, ResultadoDoAcesso.Aceito, null, http.Connection.RemoteIpAddress);
        return Results.Ok(new RelatorioResponse(total, aceitos, total - aceitos, indevidas, porEvento, porMotivo, recentes));
    }

    // Separador ponto e vírgula e BOM: o Excel em português abre direto, com acentos certos.
    private static async Task<IResult> Csv(HttpContext http, ClaimsPrincipal usuario, MinisterioDbContext db, ServicoDeAdmins admins, ServicoDeAuditoria auditoria, CancellationToken cancelamento)
    {
        var usuarioId = await ExigirMinistroAsync(http, usuario, db, admins, auditoria, cancelamento);
        var texto = new StringBuilder("quando;usuario;evento;resultado;motivo;ip;detalhe\r\n");
        foreach (var a in (await Linhas(db, LimiteDoCsv, cancelamento)).Select(Para))
        {
            texto.Append(CultureInfo.InvariantCulture, $"{a.Quando:yyyy-MM-dd HH:mm:ss};{Celula(a.Usuario)};{a.Evento};{a.Resultado};{Celula(a.Motivo)};{Celula(a.Ip)};{Celula(a.Detalhe)}\r\n");
        }

        await auditoria.RegistrarAsync(usuarioId, EventoDeAcesso.Relatorio, ResultadoDoAcesso.Aceito, null, http.Connection.RemoteIpAddress);
        return Results.File(new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(texto.ToString())).ToArray(), "text/csv; charset=utf-8", "acessos.csv");
    }

    // Evita que um nome começado em = + - @ vire fórmula na planilha, e que um ; quebre a coluna.
    private static string Celula(string? valor) =>
        valor is null ? string.Empty : (valor.Length > 0 && "=+-@".Contains(valor[0], StringComparison.Ordinal) ? "'" : string.Empty) + valor.Replace(';', ',').Replace('\r', ' ').Replace('\n', ' ');

    private static Task<List<LinhaDeAcesso>> Linhas(MinisterioDbContext db, int quantidade, CancellationToken cancelamento) =>
        db.Acessos.AsNoTracking()
            .OrderByDescending(a => a.OcorridoEm).ThenByDescending(a => a.Id)
            .Take(quantidade)
            .GroupJoin(db.Usuarios, a => a.UsuarioId, u => u.Id, (a, us) => new { a, us })
            .SelectMany(x => x.us.DefaultIfEmpty(), (x, u) => new LinhaDeAcesso(x.a.OcorridoEm, u == null ? null : u.Nome, x.a.Evento, x.a.Resultado, x.a.Motivo, x.a.Ip, x.a.Detalhe))
            .ToListAsync(cancelamento);

    private static AcessoDto Para(LinhaDeAcesso l) => new(l.Quando, l.Usuario, Codigo.De(l.Evento), Codigo.De(l.Resultado), l.Motivo, l.Ip, l.Detalhe);

    private sealed record LinhaDeAcesso(DateTimeOffset Quando, string? Usuario, EventoDeAcesso Evento, ResultadoDoAcesso Resultado, string? Motivo, string? Ip, string? Detalhe);
}
