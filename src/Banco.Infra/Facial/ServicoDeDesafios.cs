using System.Net;
using Banco.Dominio;
using Banco.Dominio.Biometria;
using Banco.Dominio.Transferencias;
using Banco.Infra.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Banco.Infra.Facial;

/// <summary>Um desafio recém-consumido. <c>UsadoEm</c> é o carimbo gravado, que a devolução usa para só desfazer o próprio consumo.</summary>
public sealed record DesafioConsumido(Guid? UsuarioId, decimal Limiar, DateTime UsadoEm);

/// <summary>O que o desafio de uma operação precisa carregar para ser consumido: de quem, de qual transferência e o hash dela.</summary>
public sealed record Amarra(Guid UsuarioId, Guid TransferenciaId, byte[] Hash);

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

    /// <summary>
    /// Um desafio por transferência (índice único): se ainda não foi usado, devolve o mesmo (sem zerar as devoluções já gastas,
    /// e ele nunca vence antes da intenção, que nasceu antes); se já foi consumido, troca por um novo.
    /// </summary>
    public async Task<DesafioBiometrico> AbrirDeTransferenciaAsync(Guid usuarioId, Transferencia transferencia, decimal limiar, IPAddress? ip, CancellationToken cancelamento = default)
    {
        var existente = await banco.Desafios.AsNoTracking().SingleOrDefaultAsync(d => d.TransferenciaId == transferencia.Id, cancelamento);
        if (existente is { UsadoEm: null })
        {
            return existente;
        }

        if (existente is not null)
        {
            // Já consumido sem fechar a transferência (ex.: conta bloqueada na hora): nasce outro.
            await banco.Desafios.Where(d => d.Id == existente.Id).ExecuteDeleteAsync(cancelamento);
        }

        var desafio = DesafioBiometrico.AbrirDeTransferencia(usuarioId, transferencia, limiar, ip, relogio.GetUtcNow());
        banco.Add(desafio);
        try
        {
            await banco.SaveChangesAsync(cancelamento);
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Duas aberturas simultâneas: a outra ganhou, e o desafio dela vale igual.
            banco.ChangeTracker.Clear();
            return await banco.Desafios.AsNoTracking().SingleAsync(d => d.TransferenciaId == transferencia.Id, cancelamento);
        }

        return desafio;
    }

    /// <summary>
    /// Nulo quando o desafio não existe, é de outra finalidade, venceu ou já foi usado: o chamador não distingue os casos.
    /// Com <paramref name="amarra"/>, também exige o dono, a transferência e o hash gravados na abertura.
    /// </summary>
    public async Task<DesafioConsumido?> ConsumirAsync(Guid desafioId, FinalidadeDoDesafio finalidade, MetodoDoDesafio metodo, Amarra? amarra = null, CancellationToken cancelamento = default)
    {
        var agora = relogio.GetUtcNow();
        return await ExecutarAsync(
            """
            UPDATE desafios_biometricos
               SET usado_em = @agora, metodo = @metodo
             WHERE id = @id AND finalidade = @finalidade AND usado_em IS NULL AND expira_em > @agora
            """ + (amarra is null ? "" : " AND usuario_id = @usuario AND transferencia_id = @transferencia AND hash_operacao = @hash") + """

            RETURNING usuario_id, limiar_aplicado, usado_em
            """,
            cmd =>
            {
                cmd.Parameters.AddWithValue("agora", NpgsqlDbType.TimestampTz, agora.UtcDateTime);
                cmd.Parameters.AddWithValue("metodo", Codigo.De(metodo));
                cmd.Parameters.AddWithValue("id", desafioId);
                cmd.Parameters.AddWithValue("finalidade", Codigo.De(finalidade));
                if (amarra is not null)
                {
                    cmd.Parameters.AddWithValue("usuario", amarra.UsuarioId);
                    cmd.Parameters.AddWithValue("transferencia", amarra.TransferenciaId);
                    cmd.Parameters.AddWithValue("hash", amarra.Hash);
                }
            },
            cancelamento);
    }

    /// <summary>
    /// Compara a foto com a referência. Se a foto não pôde ser avaliada, o desafio volta antes de a falha seguir adiante:
    /// só o serviço facial fora do ar não conta como uma das 5 devoluções.
    /// </summary>
    public async Task<ResultadoDaVerificacao> VerificarAsync(IClienteFacial facial, Guid desafioId, DesafioConsumido consumo, byte[] imagem, byte[] referencia, string? idDaRequisicao, CancellationToken cancelamento = default)
    {
        try
        {
            return await facial.VerificarAsync(imagem, referencia, consumo.Limiar, idDaRequisicao, cancelamento);
        }
        catch (Exception e) when (e is RostoNaoDetectadoException or VariosRostosException or ImagemInvalidaException or ServicoFacialIndisponivelException)
        {
            await DevolverAsync(desafioId, consumo, contar: e is not ServicoFacialIndisponivelException, CancellationToken.None);
            throw;
        }
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
