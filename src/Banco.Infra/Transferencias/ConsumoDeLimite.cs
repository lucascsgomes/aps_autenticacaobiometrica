using Banco.Dominio.Contas;
using Banco.Dominio.Transferencias;
using Banco.Infra.Persistencia;
using Microsoft.EntityFrameworkCore;

namespace Banco.Infra.Transferencias;

/// <summary>Quanto a conta já enviou em cada janela. Sem estorno no app, a soma das concluídas não tem desconto.</summary>
internal static class ConsumoDeLimite
{
    public static async Task<(long Dia, long Noite)> SomarAsync(BancoDbContext banco, Guid contaId, PoliticaDeLimite.Janelas janelas, CancellationToken cancelamento)
    {
        var enviadas = banco.Transferencias.Where(t =>
            t.ContaOrigemId == contaId
            && t.Status == StatusTransferencia.Concluida
            && (t.Tipo == TipoTransferencia.Pix || t.Tipo == TipoTransferencia.Ted));

        var dia = await enviadas
            .Where(t => t.ConcluidaEm >= janelas.DiaInicio && t.ConcluidaEm < janelas.DiaFim)
            .SumAsync(t => (long?)t.ValorCentavos, cancelamento) ?? 0;

        long noite = 0;
        if (janelas.Noite is var (inicio, fim))
        {
            noite = await enviadas
                .Where(t => t.ConcluidaEm >= inicio && t.ConcluidaEm < fim)
                .SumAsync(t => (long?)t.ValorCentavos, cancelamento) ?? 0;
        }

        return (dia, noite);
    }
}
