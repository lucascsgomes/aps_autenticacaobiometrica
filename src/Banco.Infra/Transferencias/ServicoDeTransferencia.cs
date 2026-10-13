using System.Net;
using Banco.Dominio;
using Banco.Dominio.Biometria;
using Banco.Dominio.Chaves;
using Banco.Dominio.Contas;
using Banco.Dominio.Transferencias;
using Banco.Dominio.Usuarios;
using Banco.Infra.Autenticacao;
using Banco.Infra.Facial;
using Banco.Infra.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Banco.Infra.Transferencias;

public sealed record IntencaoCriada(
    Guid TransferenciaId,
    DateTimeOffset ExpiraEm,
    string OrigemNome,
    string OrigemContaMascarada,
    string DestinoNomeMascarado,
    string ChaveMascarada,
    long ValorCentavos,
    long SaldoAposCentavos,
    bool PodeUsarRosto);

public sealed record Parte(string Nome, string Agencia, string ContaMascarada, bool EhOCliente);

/// <summary>Dados do comprovante já filtrados para quem pede: saldo só do próprio lado e a outra parte sem nome completo.</summary>
public sealed record ComprovanteDados(
    Guid TransferenciaId,
    string E2eId,
    TipoTransferencia Tipo,
    long ValorCentavos,
    string? Descricao,
    DateTimeOffset ConcluidaEm,
    MetodoConfirmacao Metodo,
    Parte Origem,
    Parte Destino,
    long SaldoAposCentavos);

public sealed record EventoDeHistorico(string? De, string Para, DateTimeOffset Em);

public sealed record SituacaoDaTransferencia(
    Guid TransferenciaId,
    StatusTransferencia Status,
    long ValorCentavos,
    string? MotivoFalha,
    DateTimeOffset CriadaEm,
    DateTimeOffset? ConcluidaEm,
    IReadOnlyList<EventoDeHistorico> Historico);

public sealed class ServicoDeTransferencia(
    BancoDbContext banco,
    ExecutorDeTransferencia executor,
    ServicoDeSenha senhas,
    ServicoDeDesafios desafios,
    IClienteFacial facial,
    CifraDeTemplate cifra,
    LimiaresFaciais limiares,
    TimeProvider relogio)
{
    public const string NomeDaInstituicao = "Banco APS";

    /// <summary>
    /// Grava o que será confirmado, sem mover dinheiro. A checagem de saldo aqui é de cortesia: a que vale roda dentro da transação do dinheiro.
    /// Uma intenção pendente por conta: a nova expira a anterior.
    /// </summary>
    public async Task<IntencaoCriada> CriarIntencaoAsync(Guid usuarioId, string chaveDestino, long valorCentavos, string? descricao, CancellationToken cancelamento = default)
    {
        var tipo = ChaveTransferencia.Classificar(chaveDestino) ?? throw new ChaveInvalidaException();
        var destino = await banco.Chaves
            .Where(k => k.Valor == chaveDestino)
            .Join(banco.Contas, k => k.ContaId, c => c.Id, (k, c) => new { Conta = c })
            .GroupJoin(banco.Usuarios, x => x.Conta.UsuarioId, u => u.Id, (x, us) => new { x.Conta, Us = us })
            .SelectMany(x => x.Us.DefaultIfEmpty(), (x, u) => new { x.Conta, Nome = u == null ? NomeDaInstituicao : u.Nome })
            .SingleOrDefaultAsync(cancelamento) ?? throw new ChaveNaoEncontradaException();
        var origem = await banco.Contas
            .Where(c => c.UsuarioId == usuarioId)
            .Join(banco.Usuarios, c => c.UsuarioId, u => u.Id, (c, u) => new { Conta = c, u.Nome })
            .SingleAsync(cancelamento);

        if (origem.Conta.Id == destino.Conta.Id)
        {
            throw new MesmaContaException();
        }

        if (destino.Conta.Status != StatusConta.Ativa)
        {
            throw new DestinoIndisponivelException();
        }

        var valor = Dinheiro.DeCentavos(valorCentavos);
        if (!origem.Conta.PodeDebitar(valor))
        {
            throw new SaldoInsuficienteException();
        }

        var agora = relogio.GetUtcNow();
        if (origem.Conta.Tipo == TipoConta.Cliente)
        {
            // Checagem de cortesia, sem lock: a que vale é a do executor.
            var janelas = PoliticaDeLimite.Calcular(origem.Conta, agora);
            var (dia, noite) = await ConsumoDeLimite.SomarAsync(banco, origem.Conta.Id, janelas, cancelamento);
            var motivo = PoliticaDeLimite.MotivoDaRecusa(origem.Conta, janelas, dia, noite, valorCentavos);
            if (motivo == MotivoFalha.LimiteNoturno)
            {
                throw new LimiteNoturnoExcedidoException();
            }

            if (motivo == MotivoFalha.LimiteDiurno)
            {
                throw new LimiteDiurnoExcedidoException();
            }
        }

        foreach (var antiga in await banco.Transferencias
            .Where(t => t.ContaOrigemId == origem.Conta.Id && t.Status == StatusTransferencia.PendenteBiometria)
            .ToListAsync(cancelamento))
        {
            antiga.Expirar();
        }

        var transferencia = Transferencia.Criar(origem.Conta.Id, destino.Conta.Id, valor, descricao, agora);
        banco.Transferencias.Add(transferencia);
        await banco.SaveChangesAsync(cancelamento);

        return new IntencaoCriada(
            transferencia.Id,
            agora + Transferencia.ValidadeDaConfirmacao,
            origem.Nome,
            Mascara.Conta(origem.Conta.Numero),
            Mascara.Nome(destino.Nome),
            Mascara.Chave(tipo, chaveDestino),
            valorCentavos,
            origem.Conta.SaldoCentavos - valorCentavos,
            await banco.Templates.AnyAsync(t => t.UsuarioId == usuarioId, cancelamento));
    }

    /// <summary>
    /// Confirma com a senha. Senha errada gasta a intenção (vira FALHA) e conta para o bloqueio; senha certa de conta bloqueada não move nada.
    /// Devolve a transferência no estado final (CONCLUIDA ou FALHA). O resto sai como exceção, sem mudar a transferência.
    /// </summary>
    public async Task<Transferencia> ConfirmarComSenhaAsync(Guid usuarioId, Guid transferenciaId, string senha, CancellationToken cancelamento = default)
    {
        var contaId = await ContaDoUsuarioAsync(usuarioId, cancelamento);
        return await ConfirmarAsync(usuarioId, contaId, transferenciaId, u => senhas.Verificar(u.SenhaHash, senha), MetodoConfirmacao.Senha, MotivoFalha.SenhaRecusada, cancelamento);
    }

    /// <summary>Abre (ou reaproveita) o desafio de rosto desta transferência. Só quem tem rosto cadastrado e só enquanto a intenção vale.</summary>
    public async Task<DesafioBiometrico> AbrirDesafioDeRostoAsync(Guid usuarioId, Guid transferenciaId, IPAddress? ip, CancellationToken cancelamento = default)
    {
        var transferencia = await PendenteDoUsuarioAsync(usuarioId, transferenciaId, cancelamento);
        if (!await banco.Templates.AnyAsync(t => t.UsuarioId == usuarioId, cancelamento))
        {
            throw new FaceNaoVerificadaException();
        }

        return await desafios.AbrirDeTransferenciaAsync(usuarioId, transferencia, limiares.Transferencia, ip, cancelamento);
    }

    /// <summary>
    /// Confirma com o rosto, no limiar de transferência. Rosto errado gasta a intenção como a senha errada; foto sem rosto, com vários,
    /// ilegível ou serviço facial fora do ar deixam a transferência pendente e devolvem o desafio.
    /// </summary>
    public async Task<Transferencia> ConfirmarComRostoAsync(Guid usuarioId, Guid transferenciaId, Guid desafioId, byte[] imagem, string? idDaRequisicao, CancellationToken cancelamento = default)
    {
        var transferencia = await PendenteDoUsuarioAsync(usuarioId, transferenciaId, cancelamento);
        var consumo = await desafios.ConsumirAsync(
                desafioId,
                FinalidadeDoDesafio.Transferencia,
                MetodoDoDesafio.Face,
                new Amarra(usuarioId, transferenciaId, DesafioBiometrico.HashDaTransferencia(usuarioId, transferencia)),
                cancelamento)
            ?? throw new FaceNaoVerificadaException();

        var template = await banco.Templates.AsNoTracking().SingleOrDefaultAsync(t => t.UsuarioId == usuarioId, cancelamento) ?? throw new FaceNaoVerificadaException();
        var referencia = cifra.Decifrar(usuarioId, template.EmbeddingCifrado, template.Nonce);
        var resultado = await desafios.VerificarAsync(facial, desafioId, consumo, imagem, referencia, idDaRequisicao, cancelamento);

        return await ConfirmarAsync(usuarioId, transferencia.ContaOrigemId, transferenciaId, _ => resultado.Corresponde, MetodoConfirmacao.Face, MotivoFalha.BiometriaRecusada, cancelamento);
    }

    private Task<Guid> ContaDoUsuarioAsync(Guid usuarioId, CancellationToken cancelamento) =>
        banco.Contas.Where(c => c.UsuarioId == usuarioId).Select(c => c.Id).SingleAsync(cancelamento);

    /// <summary>Leitura sem trava, para decidir antes de gastar desafio ou foto. A que vale é a feita sob trava em <see cref="ConfirmarAsync"/>.</summary>
    private async Task<Transferencia> PendenteDoUsuarioAsync(Guid usuarioId, Guid transferenciaId, CancellationToken cancelamento)
    {
        var contaId = await ContaDoUsuarioAsync(usuarioId, cancelamento);
        var transferencia = await banco.Transferencias.AsNoTracking().SingleOrDefaultAsync(t => t.Id == transferenciaId && t.ContaOrigemId == contaId, cancelamento)
            ?? throw new TransferenciaNaoEncontradaException();
        if (transferencia.Status != StatusTransferencia.PendenteBiometria)
        {
            throw new TransferenciaNaoPendenteException();
        }

        return transferencia.Venceu(relogio.GetUtcNow()) ? throw new TransferenciaExpiradaException() : transferencia;
    }

    private async Task<Transferencia> ConfirmarAsync(
        Guid usuarioId,
        Guid contaId,
        Guid transferenciaId,
        Func<Usuario, bool> credencialConfere,
        MetodoConfirmacao metodo,
        string motivoDaRecusa,
        CancellationToken cancelamento)
    {
        await using (var transacao = await banco.Database.BeginTransactionAsync(cancelamento))
        {
            // Trava a linha: duas confirmações simultâneas da mesma transferência não passam juntas.
            var transferencia = await banco.Transferencias
                .FromSql($"SELECT * FROM transferencias WHERE id = {transferenciaId} AND conta_origem_id = {contaId} FOR UPDATE")
                .SingleOrDefaultAsync(cancelamento) ?? throw new TransferenciaNaoEncontradaException();
            if (transferencia.Status != StatusTransferencia.PendenteBiometria)
            {
                throw new TransferenciaNaoPendenteException();
            }

            var agora = relogio.GetUtcNow();
            if (transferencia.Venceu(agora))
            {
                transferencia.Expirar();
                await banco.SaveChangesAsync(cancelamento);
                await transacao.CommitAsync(cancelamento);
                throw new TransferenciaExpiradaException();
            }

            var usuario = await banco.Usuarios.SingleAsync(u => u.Id == usuarioId, cancelamento);
            if (!credencialConfere(usuario))
            {
                if (!usuario.EstaBloqueado(agora))
                {
                    usuario.RegistrarFalhaDeCredencial(agora);
                }

                transferencia.Falhar(motivoDaRecusa);
                await banco.SaveChangesAsync(cancelamento);
                await transacao.CommitAsync(cancelamento);
                return transferencia;
            }

            if (usuario.EstaBloqueado(agora))
            {
                throw new ContaBloqueadaException(usuario.BloqueadoAte!.Value - agora);
            }

            usuario.RegistrarCredencialCorreta(agora);
            transferencia.Confirmar(metodo);
            await banco.SaveChangesAsync(cancelamento);
            await transacao.CommitAsync(cancelamento);
        }

        await executor.ExecutarAsync(transferenciaId, cancelamento);
        banco.ChangeTracker.Clear();
        return await banco.Transferencias.SingleAsync(t => t.Id == transferenciaId, cancelamento);
    }

    /// <summary>Só existe para CONCLUIDA, e só para quem enviou ou recebeu. Nos demais casos é nulo (vira 404).</summary>
    public async Task<ComprovanteDados?> ObterComprovanteAsync(Guid usuarioId, Guid transferenciaId, CancellationToken cancelamento = default)
    {
        var t = await banco.Transferencias.AsNoTracking().SingleOrDefaultAsync(x => x.Id == transferenciaId && x.Status == StatusTransferencia.Concluida, cancelamento);
        if (t is null)
        {
            return null;
        }

        var origem = await DadosDaContaAsync(t.ContaOrigemId, cancelamento);
        var destino = await DadosDaContaAsync(t.ContaDestinoId, cancelamento);
        var souOrigem = origem.UsuarioId == usuarioId;
        if (!souOrigem && destino.UsuarioId != usuarioId)
        {
            return null;
        }

        var minhaConta = souOrigem ? t.ContaOrigemId : t.ContaDestinoId;
        var saldoApos = await banco.Lancamentos
            .Where(l => l.TransferenciaId == t.Id && l.ContaId == minhaConta)
            .Select(l => l.SaldoAposCentavos)
            .SingleAsync(cancelamento);

        return new ComprovanteDados(
            t.Id,
            t.E2eId!,
            t.Tipo,
            t.ValorCentavos,
            t.Descricao,
            t.ConcluidaEm!.Value,
            t.MetodoConfirmacao ?? MetodoConfirmacao.Senha,
            new Parte(souOrigem ? origem.Nome : Mascara.Nome(origem.Nome), origem.Agencia, Mascara.Conta(origem.Numero), souOrigem),
            new Parte(Mascara.Nome(destino.Nome), destino.Agencia, Mascara.Conta(destino.Numero), !souOrigem),
            saldoApos);
    }

    /// <summary>Estado e histórico, para a tela de "status incerto" quando a resposta do confirmar se perde. Só de quem enviou.</summary>
    public async Task<SituacaoDaTransferencia?> ObterSituacaoAsync(Guid usuarioId, Guid transferenciaId, CancellationToken cancelamento = default)
    {
        var t = await banco.Transferencias.AsNoTracking()
            .Where(x => x.Id == transferenciaId && banco.Contas.Any(c => c.Id == x.ContaOrigemId && c.UsuarioId == usuarioId))
            .SingleOrDefaultAsync(cancelamento);
        if (t is null)
        {
            return null;
        }

        var historico = await banco.Database
            .SqlQuery<LinhaDeHistorico>($"SELECT de_status, para_status, em FROM transferencia_historico WHERE transferencia_id = {transferenciaId} ORDER BY id")
            .ToListAsync(cancelamento);
        return new SituacaoDaTransferencia(
            t.Id,
            t.Status,
            t.ValorCentavos,
            t.MotivoFalha,
            t.CriadaEm,
            t.ConcluidaEm,
            historico.Select(h => new EventoDeHistorico(h.DeStatus, h.ParaStatus, h.Em)).ToList());
    }

    private async Task<DadosDaConta> DadosDaContaAsync(Guid contaId, CancellationToken cancelamento) =>
        await banco.Contas.AsNoTracking()
            .Where(c => c.Id == contaId)
            .GroupJoin(banco.Usuarios, c => c.UsuarioId, u => u.Id, (c, us) => new { c, us })
            .SelectMany(x => x.us.DefaultIfEmpty(), (x, u) => new DadosDaConta(x.c.UsuarioId, u == null ? NomeDaInstituicao : u.Nome, x.c.Agencia, x.c.Numero))
            .SingleAsync(cancelamento);

    private sealed record DadosDaConta(Guid? UsuarioId, string Nome, string Agencia, string Numero);

    private sealed record LinhaDeHistorico(string? DeStatus, string ParaStatus, DateTimeOffset Em);
}
