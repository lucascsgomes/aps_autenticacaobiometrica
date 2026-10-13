using Banco.Dominio;
using Banco.Dominio.Contas;
using Banco.Infra.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Banco.Infra.Transferencias;

public sealed record LimitesDaConta(
    long DiurnoCentavos,
    long NoturnoCentavos,
    TimeOnly InicioNoturno,
    TimeOnly FimNoturno,
    long UsadoNaJanelaAtualCentavos,
    bool JanelaNoturna);

public sealed class ServicoDeLimites(BancoDbContext banco, TimeProvider relogio)
{
    public async Task<LimitesDaConta> ObterAsync(Guid usuarioId, CancellationToken cancelamento = default)
    {
        var conta = await banco.Contas.AsNoTracking().SingleAsync(c => c.UsuarioId == usuarioId, cancelamento);
        return await MontarAsync(conta, cancelamento);
    }

    /// <summary>Reduzir vale na hora. Qualquer aumento (ou encurtar o período noturno) exige confirmação: sem <paramref name="confirmado"/> responde 403 STEP_UP_REQUIRED.</summary>
    public async Task<LimitesDaConta> AlterarAsync(Guid usuarioId, long? diurnoCentavos, long? noturnoCentavos, TimeOnly? inicioNoturno, bool confirmado = false, CancellationToken cancelamento = default)
    {
        var conta = await banco.Contas.SingleAsync(c => c.UsuarioId == usuarioId, cancelamento);
        var diurno = diurnoCentavos ?? conta.LimiteDiurnoCentavos;
        var noturno = noturnoCentavos ?? conta.LimiteNoturnoCentavos;
        var inicio = inicioNoturno ?? conta.InicioNoturno;

        if (!confirmado && conta.ExigeConfirmacao(diurno, noturno, inicio))
        {
            throw new ConfirmacaoNecessariaException("ALTERAR_LIMITE");
        }

        conta.DefinirLimites(diurno, noturno, inicio);
        await banco.SaveChangesAsync(cancelamento);
        return await MontarAsync(conta, cancelamento);
    }

    private async Task<LimitesDaConta> MontarAsync(Conta conta, CancellationToken cancelamento)
    {
        var janelas = PoliticaDeLimite.Calcular(conta, relogio.GetUtcNow());
        var (dia, noite) = await ConsumoDeLimite.SomarAsync(banco, conta.Id, janelas, cancelamento);
        return new LimitesDaConta(
            conta.LimiteDiurnoCentavos,
            conta.LimiteNoturnoCentavos,
            conta.InicioNoturno,
            conta.FimNoturno,
            janelas.Noite is null ? dia : noite,
            janelas.Noite is not null);
    }
}
