using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Ministerio.Dominio.Biometria;

public enum FinalidadeDoDesafio
{
    Cadastro,
    Login,
    AcessoNivel2,
    AcessoNivel3,
    TrocarSenha,
    Recadastro,
    AlterarAcesso,
}

public enum MetodoDoDesafio
{
    Face,
    Senha,
}

/// <summary>
/// Convite de uso único para provar quem é: rosto ou senha, uma tentativa só. Quem consome e devolve o desafio é o SQL do
/// <c>ServicoDeDesafios</c> (um UPDATE atômico), então a entidade só serve para abrir e para o mapeamento.
/// </summary>
public sealed class DesafioBiometrico
{
    public static readonly TimeSpan Validade = TimeSpan.FromSeconds(90);
    public const int MaximoDeDevolucoes = 5;

    public Guid Id { get; private set; }

    /// <summary>Nulo só no login de um CPF que não existe: a resposta é igual e o fim dela é FACE_NOT_VERIFIED.</summary>
    public Guid? UsuarioId { get; private set; }

    public FinalidadeDoDesafio Finalidade { get; private set; }
    public byte[] Nonce { get; private set; } = default!;
    public byte[]? HashOperacao { get; private set; }
    public decimal LimiarAplicado { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; }
    public DateTimeOffset ExpiraEm { get; private set; }
    public DateTimeOffset? UsadoEm { get; private set; }
    public MetodoDoDesafio? Metodo { get; private set; }
    public short DevolvidoVezes { get; private set; }
    public IPAddress? Ip { get; private set; }

    private DesafioBiometrico()
    {
    }

    public static DesafioBiometrico AbrirDeLogin(Guid? usuarioId, decimal limiar, IPAddress? ip, DateTimeOffset agora) => new()
    {
        Id = Guid.NewGuid(),
        UsuarioId = usuarioId,
        Finalidade = FinalidadeDoDesafio.Login,
        Nonce = RandomNumberGenerator.GetBytes(32),
        LimiarAplicado = limiar,
        CriadoEm = agora,
        ExpiraEm = agora + Validade,
        Ip = ip,
    };

    /// <summary>
    /// Amarra o desafio a uma ação sensível: dono, finalidade, parâmetros em JSON canônico (chaves em ordem) e o nonce do próprio desafio.
    /// O rosto aprovado vale para aquele acesso (ou aquela troca) e para mais nada.
    /// </summary>
    public static byte[] HashDaAcao(Guid usuarioId, FinalidadeDoDesafio finalidade, string parametrosCanonicos, byte[] nonce) =>
        SHA256.HashData(Encoding.UTF8.GetBytes($"{usuarioId}|{Codigo.De(finalidade)}|{parametrosCanonicos}|{Convert.ToHexString(nonce)}"));

    public static DesafioBiometrico AbrirDeAcao(Guid usuarioId, FinalidadeDoDesafio finalidade, string parametrosCanonicos, decimal limiar, IPAddress? ip, DateTimeOffset agora)
    {
        var nonce = RandomNumberGenerator.GetBytes(32);
        return new()
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuarioId,
            Finalidade = finalidade,
            Nonce = nonce,
            HashOperacao = HashDaAcao(usuarioId, finalidade, parametrosCanonicos, nonce),
            LimiarAplicado = limiar,
            CriadoEm = agora,
            ExpiraEm = agora + Validade,
            Ip = ip,
        };
    }
}
