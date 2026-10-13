using Banco.Dominio;
using Banco.Infra.Facial;

namespace Banco.Tests.Integracao;

/// <summary>Face-service de mentira: devolve um vetor fixo, ou a falha que o teste mandar. Guarda o que recebeu.</summary>
public sealed class ClienteFacialFalso : IClienteFacial
{
    public static readonly byte[] Vetor = Enumerable.Range(0, 512).Select(i => (byte)(i * 7 % 251)).ToArray();

    private int _chamadas;

    /// <summary>Falha da próxima chamada em diante. Nulo = tudo certo.</summary>
    public NegocioException? Falha { get; set; }

    public int Chamadas => _chamadas;

    public byte[]? UltimaImagem { get; private set; }

    public Task<byte[]> CadastrarAsync(byte[] imagem, string? idDaRequisicao, CancellationToken cancelamento = default)
    {
        Interlocked.Increment(ref _chamadas);
        UltimaImagem = imagem;
        return Falha is { } falha ? Task.FromException<byte[]>(falha) : Task.FromResult(Vetor);
    }
}
