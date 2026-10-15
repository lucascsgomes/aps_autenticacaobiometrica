using Ministerio.Dominio;
using Ministerio.Infra.Facial;

namespace Ministerio.Tests.Integracao;

/// <summary>Face-service de mentira: devolve um vetor fixo, ou a falha que o teste mandar. Guarda o que recebeu.</summary>
public sealed class ClienteFacialFalso : IClienteFacial
{
    public static readonly byte[] Vetor = Enumerable.Range(0, 512).Select(i => (byte)(i * 7 % 251)).ToArray();

    private int _chamadas;

    /// <summary>Falha da próxima chamada em diante. Nulo = tudo certo.</summary>
    public NegocioException? Falha { get; set; }

    /// <summary>Resultado das próximas verificações (rosto bate ou não).</summary>
    public bool Corresponde { get; set; } = true;

    public int Chamadas => _chamadas;

    public int Verificacoes { get; private set; }

    public byte[]? UltimaReferencia { get; private set; }

    public decimal? UltimoLimiar { get; private set; }

    public byte[]? UltimaImagem { get; private set; }

    /// <summary>Id do candidato que a próxima identificação devolve. Nulo = ninguém foi identificado.</summary>
    public string? IdentificarComo { get; set; }

    public bool Ambiguo { get; set; }

    public int Identificacoes { get; private set; }

    public IReadOnlyList<Candidato>? UltimosCandidatos { get; private set; }

    public decimal? UltimaMargem { get; private set; }

    public Task<ResultadoDaIdentificacao> IdentificarAsync(byte[] imagem, IReadOnlyList<Candidato> candidatos, decimal limiar, decimal margem, string? idDaRequisicao, CancellationToken cancelamento = default)
    {
        Interlocked.Increment(ref _chamadas);
        Identificacoes++;
        UltimaImagem = imagem;
        UltimoLimiar = limiar;
        UltimaMargem = margem;
        UltimosCandidatos = candidatos;
        if (Falha is { } falha)
        {
            return Task.FromException<ResultadoDaIdentificacao>(falha);
        }

        var achou = IdentificarComo is not null && !Ambiguo;
        return Task.FromResult(new ResultadoDaIdentificacao(achou, achou ? IdentificarComo : null, achou ? 0.9 : 0.1, Ambiguo));
    }

    public Task<byte[]> CadastrarAsync(byte[] imagem, string? idDaRequisicao, CancellationToken cancelamento = default)
    {
        Interlocked.Increment(ref _chamadas);
        UltimaImagem = imagem;
        return Falha is { } falha ? Task.FromException<byte[]>(falha) : Task.FromResult(Vetor);
    }

    public Task<ResultadoDaVerificacao> VerificarAsync(byte[] imagem, byte[] referencia, decimal limiar, string? idDaRequisicao, CancellationToken cancelamento = default)
    {
        Interlocked.Increment(ref _chamadas);
        Verificacoes++;
        UltimaImagem = imagem;
        UltimaReferencia = referencia;
        UltimoLimiar = limiar;
        return Falha is { } falha
            ? Task.FromException<ResultadoDaVerificacao>(falha)
            : Task.FromResult(new ResultadoDaVerificacao(Corresponde, Corresponde ? 0.9 : 0.1));
    }
}
