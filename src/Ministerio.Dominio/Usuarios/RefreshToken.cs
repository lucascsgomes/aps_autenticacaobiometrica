namespace Ministerio.Dominio.Usuarios;

/// <summary>
/// Token de renovação opaco. Só o SHA-256 fica na base. Cada login abre uma família: usar de novo um token
/// já usado indica roubo, e a família inteira é revogada.
/// </summary>
public sealed class RefreshToken
{
    public Guid Id { get; private set; }
    public Guid UsuarioId { get; private set; }
    public Guid FamiliaId { get; private set; }
    public byte[] TokenHash { get; private set; } = default!;
    public DateTimeOffset CriadoEm { get; private set; }
    public DateTimeOffset ExpiraEm { get; private set; }
    public DateTimeOffset? UsadoEm { get; private set; }
    public DateTimeOffset? RevogadoEm { get; private set; }
    public Guid? SubstituidoPor { get; private set; }

    private RefreshToken()
    {
    }

    public static RefreshToken Emitir(Guid usuarioId, Guid familiaId, byte[] tokenHash, DateTimeOffset agora, TimeSpan validade) =>
        tokenHash is not { Length: 32 }
            ? throw new ArgumentException("O hash do token deve ter 32 bytes (SHA-256).", nameof(tokenHash))
            : new RefreshToken
            {
                Id = Guid.NewGuid(),
                UsuarioId = usuarioId,
                FamiliaId = familiaId,
                TokenHash = tokenHash,
                CriadoEm = agora,
                ExpiraEm = agora + validade,
            };

    public bool JaFoiUsadoOuRevogado => UsadoEm is not null || RevogadoEm is not null;

    public bool Expirou(DateTimeOffset agora) => ExpiraEm <= agora;

    public void MarcarComoUsado(Guid substitutoId, DateTimeOffset agora)
    {
        UsadoEm = agora;
        SubstituidoPor = substitutoId;
    }
}
