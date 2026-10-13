using Banco.Dominio;
using Banco.Dominio.Chaves;
using Banco.Infra.Persistencia;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Banco.Infra.Autenticacao;

/// <summary>Chaves de transferência da própria conta: até 5, valor único no banco inteiro.</summary>
public sealed class ServicoDeChaves(BancoDbContext banco, TimeProvider relogio)
{
    public const int Maximo = 5;

    /// <summary>
    /// Confere o que dá para conferir sem gastar a confirmação do usuário: formato, limite de 5 e valor já em uso.
    /// A chave aleatória não tem valor ainda; o servidor o gera em <see cref="CriarAsync"/>.
    /// </summary>
    public async Task<Guid> PrepararAsync(Guid usuarioId, TipoChave tipo, string? valor, CancellationToken cancelamento = default)
    {
        if (tipo != TipoChave.Aleatoria && ChaveTransferencia.Classificar(valor) != tipo)
        {
            throw new ChaveInvalidaException();
        }

        var contaId = await banco.Contas.Where(c => c.UsuarioId == usuarioId).Select(c => c.Id).SingleAsync(cancelamento);
        if (await banco.Chaves.CountAsync(k => k.ContaId == contaId, cancelamento) >= Maximo)
        {
            throw new LimiteDeChavesException();
        }

        return valor is not null && await banco.Chaves.AnyAsync(k => k.Valor == valor, cancelamento) ? throw new ChaveJaExisteException() : contaId;
    }

    // shortcut: contar e inserir não são atômicos, então duas criações simultâneas podem passar de 5; um gatilho no banco fecharia a brecha.
    public async Task<ChaveTransferencia> CriarAsync(Guid contaId, TipoChave tipo, string? valor, CancellationToken cancelamento = default)
    {
        var chave = ChaveTransferencia.Criar(contaId, tipo, valor ?? Guid.NewGuid().ToString(), relogio.GetUtcNow());
        banco.Chaves.Add(chave);
        try
        {
            await banco.SaveChangesAsync(cancelamento);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ChaveJaExisteException();
        }

        return chave;
    }
}
