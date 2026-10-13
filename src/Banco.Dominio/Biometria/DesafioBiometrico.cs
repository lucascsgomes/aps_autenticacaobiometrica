using System.Net;
using System.Security.Cryptography;
using System.Text;
using Banco.Dominio.Transferencias;

namespace Banco.Dominio.Biometria;

public enum FinalidadeDoDesafio
{
    Cadastro,
    Login,
    Transferencia,
    AlterarLimite,
    NovaChave,
    NovoFavorecido,
    TrocarSenha,
    Recadastro,
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
    public Guid? TransferenciaId { get; private set; }
    public long? ValorCentavos { get; private set; }
    public Guid? ContaDestinoId { get; private set; }
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

    /// <summary>Amarra o desafio a esta transferência exata: outro usuário, outra transferência ou outro valor não o consomem.</summary>
    public static byte[] HashDaTransferencia(Guid usuarioId, Transferencia t) =>
        SHA256.HashData(Encoding.UTF8.GetBytes($"TRANSFERENCIA|{usuarioId}|{t.Id}|{t.ContaOrigemId}|{t.ContaDestinoId}|{t.ValorCentavos}"));

    /// <summary>
    /// Amarra o desafio a uma ação sensível: dono, finalidade, parâmetros em JSON canônico (chaves em ordem) e o nonce do próprio desafio.
    /// Rosto ou senha aprovados valem para aquele limite ou aquela chave e para mais nada.
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

    public static DesafioBiometrico AbrirDeTransferencia(Guid usuarioId, Transferencia t, decimal limiar, IPAddress? ip, DateTimeOffset agora) => new()
    {
        Id = Guid.NewGuid(),
        UsuarioId = usuarioId,
        Finalidade = FinalidadeDoDesafio.Transferencia,
        Nonce = RandomNumberGenerator.GetBytes(32),
        TransferenciaId = t.Id,
        ValorCentavos = t.ValorCentavos,
        ContaDestinoId = t.ContaDestinoId,
        HashOperacao = HashDaTransferencia(usuarioId, t),
        LimiarAplicado = limiar,
        CriadoEm = agora,
        ExpiraEm = agora + Validade,
        Ip = ip,
    };
}
