using System.Net;
using Banco.Dominio;
using Banco.Dominio.Biometria;
using Banco.Infra.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Banco.Infra.Facial;

/// <summary>Um desafio recém-consumido. <c>UsadoEm</c> é o carimbo gravado, que a devolução usa para só desfazer o próprio consumo.</summary>
public sealed record DesafioConsumido(Guid? UsuarioId, decimal Limiar, DateTime UsadoEm);

/// <summary>Abre, consome e devolve desafios. Consumir é um UPDATE atômico: dois usos simultâneos do mesmo desafio nunca passam os dois.</summary>
public sealed class ServicoDeDesafios(BancoDbContext banco, TimeProvider relogio)
{
    public async Task<DesafioBiometrico> AbrirDeLoginAsync(Guid? usuarioId, decimal limiar, IPAddress? ip, CancellationToken cancelamento = default)
    {
        var desafio = DesafioBiometrico.AbrirDeLogin(usuarioId, limiar, ip, relogio.GetUtcNow());
        banco.Add(desafio);
        await banco.SaveChangesAsync(cancelamento);
        return desafio;
    }

    /// <summary>Nulo quando o desafio não existe, é de outra finalidade, venceu ou já foi usado: o chamador não distingue os casos.</summary>
    public async Task<DesafioConsumido?> ConsumirAsync(Guid desafioId, FinalidadeDoDesafio finalidade, MetodoDoDesafio metodo, CancellationToken cancelamento = default)
    {
        var agora = relogio.GetUtcNow();
        return await ExecutarAsync(
            """
            UPDATE desafios_biometricos
               SET usado_em = @agora, metodo = @metodo
             WHERE id = @id AND finalidade = @finalidade AND usado_em IS NULL AND expira_em > @agora
            RETURNING usuario_id, limiar_aplicado, usado_em
            """,
            cmd =>
            {
                cmd.Parameters.AddWithValue("agora", NpgsqlDbType.TimestampTz, agora.UtcDateTime);
                cmd.Parameters.AddWithValue("metodo", Codigo.De(metodo));
                cmd.Parameters.AddWithValue("id", desafioId);
                cmd.Parameters.AddWithValue("finalidade", Codigo.De(finalidade));
            },
            cancelamento);
    }

    /// <summary>
    /// Desfaz o consumo de um desafio quando a foto não deu para avaliar. <paramref name="contar"/> soma uma devolução (teto de 5);
    /// sem contar, o desafio volta de graça (serviço facial fora do ar não é culpa de quem tira a foto).
    /// </summary>
    public async Task<bool> DevolverAsync(Guid desafioId, DesafioConsumido consumo, bool contar, CancellationToken cancelamento = default)
    {
        var agora = relogio.GetUtcNow();
        var devolvido = await ExecutarAsync(
            """
            UPDATE desafios_biometricos
               SET usado_em = NULL, metodo = NULL, devolvido_vezes = devolvido_vezes + @soma
             WHERE id = @id AND usado_em = @usadoEm AND devolvido_vezes + @soma <= @teto AND expira_em > @agora
            RETURNING usuario_id, limiar_aplicado, usado_em
            """,
            cmd =>
            {
                cmd.Parameters.AddWithValue("soma", NpgsqlDbType.Smallint, (short)(contar ? 1 : 0));
                cmd.Parameters.AddWithValue("id", desafioId);
                cmd.Parameters.AddWithValue("usadoEm", NpgsqlDbType.TimestampTz, DateTime.SpecifyKind(consumo.UsadoEm, DateTimeKind.Utc));
                cmd.Parameters.AddWithValue("teto", NpgsqlDbType.Smallint, (short)DesafioBiometrico.MaximoDeDevolucoes);
                cmd.Parameters.AddWithValue("agora", NpgsqlDbType.TimestampTz, agora.UtcDateTime);
            },
            cancelamento);
        return devolvido is not null;
    }

    private async Task<DesafioConsumido?> ExecutarAsync(string sql, Action<NpgsqlCommand> parametros, CancellationToken cancelamento)
    {
        var conexao = (NpgsqlConnection)banco.Database.GetDbConnection();
        var abriuAqui = conexao.State != System.Data.ConnectionState.Open;
        if (abriuAqui)
        {
            await conexao.OpenAsync(cancelamento);
        }

        try
        {
            await using var comando = new NpgsqlCommand(sql, conexao, (NpgsqlTransaction?)banco.Database.CurrentTransaction?.GetDbTransaction());
            parametros(comando);
            await using var leitor = await comando.ExecuteReaderAsync(cancelamento);
            return await leitor.ReadAsync(cancelamento)
                ? new DesafioConsumido(
                    leitor.IsDBNull(0) ? null : leitor.GetGuid(0),
                    leitor.GetDecimal(1),
                    leitor.IsDBNull(2) ? default : DateTime.SpecifyKind(leitor.GetDateTime(2), DateTimeKind.Utc))
                : null;
        }
        finally
        {
            if (abriuAqui)
            {
                await conexao.CloseAsync();
            }
        }
    }
}
