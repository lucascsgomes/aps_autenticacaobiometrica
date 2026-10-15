namespace Ministerio.Dominio.Biometria;

/// <summary>Vetor facial já cifrado (AES-256-GCM, com o usuário como dado associado). A foto nunca é guardada. No máximo um por usuário.</summary>
public sealed class TemplateFacial
{
    public const int TamanhoCifrado = 528; // 128 floats32 (512 bytes) + tag GCM de 16
    public const int TamanhoNonce = 12;
    public const int Dimensao = 128;
    public const string ModeloAtual = "sface-2021dec";

    public Guid Id { get; private set; }
    public Guid UsuarioId { get; private set; }
    public byte[] EmbeddingCifrado { get; private set; } = default!;
    public byte[] Nonce { get; private set; } = default!;
    public short ChaveVersao { get; private set; }
    public string Modelo { get; private set; } = ModeloAtual;
    public DateTimeOffset CriadoEm { get; private set; }

    private TemplateFacial()
    {
    }

    public static TemplateFacial Criar(Guid usuarioId, byte[] embeddingCifrado, byte[] nonce, short chaveVersao, DateTimeOffset agora)
    {
        if (embeddingCifrado is not { Length: TamanhoCifrado } || nonce is not { Length: TamanhoNonce } || chaveVersao < 1)
        {
            throw new ArgumentException("Template cifrado fora do formato esperado.");
        }

        return new TemplateFacial
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuarioId,
            EmbeddingCifrado = embeddingCifrado,
            Nonce = nonce,
            ChaveVersao = chaveVersao,
            CriadoEm = agora,
        };
    }
}
