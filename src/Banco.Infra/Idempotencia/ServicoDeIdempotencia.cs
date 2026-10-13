using System.Security.Cryptography;
using System.Text;
using Banco.Infra.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Banco.Infra.Idempotencia;

/// <summary>Uma operação já reservada por (usuário, Idempotency-Key). Guarda a resposta para repetir igual num retry.</summary>
public sealed class ChaveIdempotente
{
    public Guid UsuarioId { get; init; }
    public string Chave { get; init; } = default!;
    public string Escopo { get; init; } = default!;
    public byte[] RequestHash { get; init; } = default!;
    public string Estado { get; init; } = default!;
    public short? StatusCode { get; init; }
    public string? ResponseBody { get; init; }
    public DateTimeOffset CriadaEm { get; init; }
    public DateTimeOffset ExpiraEm { get; init; }
}

public enum SituacaoDaReserva
{
    Nova,
    EmAndamento,
    Concluida,
    ChaveReusada,
}

public sealed record Reserva(SituacaoDaReserva Situacao, int? StatusCode = null, string? Corpo = null);

public sealed class ServicoDeIdempotencia(BancoDbContext banco, TimeProvider relogio)
{
    public static readonly TimeSpan Validade = TimeSpan.FromHours(24);

    public static byte[] Hash(string escopo, Guid alvo) => SHA256.HashData(Encoding.UTF8.GetBytes($"{escopo}|{alvo}"));

    /// <summary>Insere antes de processar: quem perde a corrida de inserção nunca executa a operação duas vezes.</summary>
    public async Task<Reserva> ReservarAsync(Guid usuarioId, string chave, string escopo, byte[] hash, CancellationToken cancelamento = default)
    {
        var agora = relogio.GetUtcNow();
        var expira = agora + Validade;
        var inseriu = await banco.Database.ExecuteSqlAsync($"""
            INSERT INTO idempotency_keys (usuario_id, chave, escopo, request_hash, estado, criada_em, expira_em)
            VALUES ({usuarioId}, {chave}, {escopo}, {hash}, 'PROCESSANDO', {agora}, {expira})
            ON CONFLICT (usuario_id, chave) DO NOTHING
            """, cancelamento);
        if (inseriu == 1)
        {
            return new Reserva(SituacaoDaReserva.Nova);
        }

        var existente = await banco.Set<ChaveIdempotente>().AsNoTracking()
            .SingleOrDefaultAsync(k => k.UsuarioId == usuarioId && k.Chave == chave, cancelamento);
        if (existente is null)
        {
            // A chave expirou e foi varrida entre o insert e a leitura: tenta de novo, agora ela é nova.
            return await ReservarAsync(usuarioId, chave, escopo, hash, cancelamento);
        }

        if (existente.Escopo != escopo || !CryptographicOperations.FixedTimeEquals(existente.RequestHash, hash))
        {
            return new Reserva(SituacaoDaReserva.ChaveReusada);
        }

        return existente.Estado == "CONCLUIDA"
            ? new Reserva(SituacaoDaReserva.Concluida, existente.StatusCode, existente.ResponseBody)
            : new Reserva(SituacaoDaReserva.EmAndamento);
    }

    public Task ConcluirAsync(Guid usuarioId, string chave, int statusCode, string corpo, CancellationToken cancelamento = default) =>
        banco.Database.ExecuteSqlAsync($"""
            UPDATE idempotency_keys SET estado = 'CONCLUIDA', status_code = {(short)statusCode}, response_body = {corpo}::jsonb
            WHERE usuario_id = {usuarioId} AND chave = {chave} AND estado = 'PROCESSANDO'
            """, cancelamento);

    /// <summary>Devolve a chave quando a operação falhou sem mudar nada, para o cliente poder tentar de novo com ela.</summary>
    public Task LiberarAsync(Guid usuarioId, string chave, CancellationToken cancelamento = default) =>
        banco.Database.ExecuteSqlAsync($"DELETE FROM idempotency_keys WHERE usuario_id = {usuarioId} AND chave = {chave} AND estado = 'PROCESSANDO'", cancelamento);
}
