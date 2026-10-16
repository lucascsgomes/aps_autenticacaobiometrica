using Ministerio.Dominio.Acesso;
using Ministerio.Infra.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ministerio.Infra.Facial;

/// <summary>O que o administrador escolheu: <c>TratamentoRuido</c> é <see cref="ProcessamentoFacial.SemRuido"/> quando está desligado.</summary>
public sealed record EscolhaDeProcessamento(string PreProcessamento, string TratamentoRuido, DateTimeOffset? AtualizadoEm);

/// <summary>O que o cliente do face-service precisa saber: a escolha vigente do admin.</summary>
public interface IProcessamentoFacial
{
    Task<EscolhaDeProcessamento> ObterAsync(CancellationToken cancelamento = default);
}

/// <summary>
/// Como o face-service prepara a foto antes de detectar o rosto. A escolha fica no banco (o serviço facial não guarda nada e reinicia no plano grátis)
/// e vai junto de cada chamada. Cache de 60 s: com mais de uma instância da API, a outra enxerga a mudança em até um minuto.
/// </summary>
public sealed class ProcessamentoFacial(IServiceScopeFactory escopos, TimeProvider relogio) : IProcessamentoFacial
{
    public const string SemRuido = "nenhum";
    private const string ChavePre = "face.pre_processamento";
    private const string ChaveRuido = "face.tratamento_ruido";
    private static readonly TimeSpan Validade = TimeSpan.FromSeconds(60);
    private static readonly EscolhaDeProcessamento Padrao = new("clahe", SemRuido, null);

    private EscolhaDeProcessamento? _atual;
    private DateTimeOffset _carregadoEm;

    public static IReadOnlyList<string> PreProcessamentos { get; } = ["clahe", "equalizacao", "gaussiano"];

    public static IReadOnlyList<string> TratamentosDeRuido { get; } = [SemRuido, "mediana", "abertura", "fechamento"];

    public static bool Valido(string? pre, string? ruido) =>
        pre is not null && ruido is not null && PreProcessamentos.Contains(pre) && TratamentosDeRuido.Contains(ruido);

    public async Task<EscolhaDeProcessamento> ObterAsync(CancellationToken cancelamento = default)
    {
        if (_atual is { } vigente && relogio.GetUtcNow() - _carregadoEm < Validade)
        {
            return vigente;
        }

        // Duas cargas ao mesmo tempo só repetem uma leitura: dispensa trava.
        using var escopo = escopos.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<MinisterioDbContext>();
        var linhas = await db.Configuracoes.AsNoTracking().Where(c => c.Chave == ChavePre || c.Chave == ChaveRuido).ToListAsync(cancelamento);
        var pre = linhas.SingleOrDefault(c => c.Chave == ChavePre);
        var ruido = linhas.SingleOrDefault(c => c.Chave == ChaveRuido);
        _atual = pre is null || ruido is null || !Valido(pre.Valor, ruido.Valor)
            ? Padrao
            : new EscolhaDeProcessamento(pre.Valor, ruido.Valor, pre.AtualizadoEm > ruido.AtualizadoEm ? pre.AtualizadoEm : ruido.AtualizadoEm);
        _carregadoEm = relogio.GetUtcNow();
        return _atual;
    }

    /// <summary>Grava no banco do chamador e já vale para as próximas fotos desta instância.</summary>
    public async Task DefinirAsync(MinisterioDbContext db, string pre, string ruido, CancellationToken cancelamento = default)
    {
        var agora = relogio.GetUtcNow();
        foreach (var (chave, valor) in new[] { (ChavePre, pre), (ChaveRuido, ruido) })
        {
            var linha = await db.Configuracoes.SingleOrDefaultAsync(c => c.Chave == chave, cancelamento);
            if (linha is null)
            {
                db.Configuracoes.Add(Configuracao.Criar(chave, valor, agora));
            }
            else
            {
                linha.Definir(valor, agora);
            }
        }

        await db.SaveChangesAsync(cancelamento);
        _atual = new EscolhaDeProcessamento(pre, ruido, agora);
        _carregadoEm = agora;
    }
}
