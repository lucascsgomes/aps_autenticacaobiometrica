using System.Security.Cryptography;
using Ministerio.Dominio.Biometria;

namespace Ministerio.Infra.Facial;

/// <summary>
/// AES-256-GCM sobre o vetor facial, com o id do usuário como dado associado: um template copiado para a linha de outro usuário
/// não decifra. A chave vem de fora da base (variável de ambiente ou cofre) e a versão dela fica gravada com o template.
/// </summary>
public sealed class CifraDeTemplate
{
    private const int TamanhoEmbedding = TemplateFacial.Dimensao * sizeof(float);
    private const int TamanhoTag = 16;

    private readonly byte[]? _chave;

    public CifraDeTemplate(string? chaveBase64, short versao)
    {
        Versao = versao;
        if (string.IsNullOrWhiteSpace(chaveBase64))
        {
            return;
        }

        try
        {
            _chave = Convert.FromBase64String(chaveBase64);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("Biometria:ChaveCifra precisa estar em base64.");
        }

        if (_chave.Length != 32)
        {
            throw new InvalidOperationException("Biometria:ChaveCifra precisa ter 32 bytes (AES-256) em base64.");
        }
    }

    public short Versao { get; }

    public bool Configurada => _chave is not null;

    public (byte[] Cifrado, byte[] Nonce) Cifrar(Guid usuarioId, byte[] embedding)
    {
        if (embedding.Length != TamanhoEmbedding)
        {
            throw new ArgumentException("Embedding deve ter 128 floats de 32 bits.", nameof(embedding));
        }

        var nonce = RandomNumberGenerator.GetBytes(TemplateFacial.TamanhoNonce);
        var saida = new byte[TamanhoEmbedding + TamanhoTag];
        using var aes = new AesGcm(Chave(), TamanhoTag);
        aes.Encrypt(nonce, embedding, saida.AsSpan(0, TamanhoEmbedding), saida.AsSpan(TamanhoEmbedding, TamanhoTag), usuarioId.ToByteArray());
        return (saida, nonce);
    }

    /// <summary>Lança <see cref="CryptographicException"/> se o dado, o nonce, o usuário ou a chave não conferem.</summary>
    public byte[] Decifrar(Guid usuarioId, byte[] cifrado, byte[] nonce)
    {
        var embedding = new byte[TamanhoEmbedding];
        using var aes = new AesGcm(Chave(), TamanhoTag);
        aes.Decrypt(nonce, cifrado.AsSpan(0, TamanhoEmbedding), cifrado.AsSpan(TamanhoEmbedding, TamanhoTag), embedding, usuarioId.ToByteArray());
        return embedding;
    }

    private byte[] Chave() => _chave ?? throw new InvalidOperationException("Configure Biometria:ChaveCifra (32 bytes em base64) para guardar templates faciais.");
}
