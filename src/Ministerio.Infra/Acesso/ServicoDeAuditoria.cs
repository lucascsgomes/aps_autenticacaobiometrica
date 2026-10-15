using System.Net;
using Ministerio.Dominio.Acesso;
using Ministerio.Infra.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Ministerio.Infra.Acesso;

/// <summary>Grava cada tentativa de acesso, aceita ou recusada, para o relatório de segurança. Nunca grava foto, vetor nem score.</summary>
public sealed class ServicoDeAuditoria(MinisterioDbContext db, TimeProvider relogio)
{
    public async Task RegistrarAsync(Guid? usuarioId, EventoDeAcesso evento, ResultadoDoAcesso resultado, string? motivo, IPAddress? ip, string? detalhe = null)
    {
        // A tentativa pode ter falhado no meio de uma gravação: limpa o contexto para o registro não herdar a sujeira.
        db.ChangeTracker.Clear();
        db.Acessos.Add(RegistroDeAcesso.Criar(relogio.GetUtcNow(), usuarioId, evento, resultado, motivo, ip?.ToString(), detalhe));
        await db.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>Uma recusa de quem informou só o CPF: se o CPF existe, o relatório mostra quem tentou.</summary>
    public async Task RecusarPorCpfAsync(string? cpf, EventoDeAcesso evento, string motivo, IPAddress? ip)
    {
        db.ChangeTracker.Clear();
        Guid? usuarioId = cpf is null
            ? null
            : await db.Usuarios.Where(u => u.Cpf == cpf).Select(u => (Guid?)u.Id).SingleOrDefaultAsync(CancellationToken.None);
        await RegistrarAsync(usuarioId, evento, ResultadoDoAcesso.Recusado, motivo, ip);
    }

    /// <summary>Uma recusa de quem só mandou o desafio de login: o dono dele, se tiver, é quem tentou.</summary>
    public async Task RecusarPorDesafioAsync(Guid desafioId, EventoDeAcesso evento, string motivo, IPAddress? ip)
    {
        db.ChangeTracker.Clear();
        var usuarioId = await db.Desafios.Where(d => d.Id == desafioId).Select(d => d.UsuarioId).SingleOrDefaultAsync(CancellationToken.None);
        await RegistrarAsync(usuarioId, evento, ResultadoDoAcesso.Recusado, motivo, ip);
    }
}
