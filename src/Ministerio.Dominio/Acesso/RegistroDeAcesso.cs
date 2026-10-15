namespace Ministerio.Dominio.Acesso;

public enum EventoDeAcesso
{
    LoginSenha,
    LoginRosto,
    LoginIdentificacao,
    ConsultaNivel1,
    ConsultaNivel2,
    ConsultaNivel3,
    Relatorio,
    AlteracaoDeAcesso,
}

public enum ResultadoDoAcesso
{
    Aceito,
    Recusado,
}

/// <summary>Uma linha do relatório de segurança: quem tentou o quê, quando, e se foi aceito. Nunca guarda foto, vetor nem score.</summary>
public sealed class RegistroDeAcesso
{
    public long Id { get; private set; }
    public DateTimeOffset OcorridoEm { get; private set; }

    /// <summary>Nulo quando a pessoa não foi identificada (rosto desconhecido, CPF que não existe).</summary>
    public Guid? UsuarioId { get; private set; }

    public EventoDeAcesso Evento { get; private set; }
    public ResultadoDoAcesso Resultado { get; private set; }

    /// <summary>O <c>code</c> do erro quando recusado (FACE_NOT_VERIFIED, NIVEL_INSUFICIENTE...).</summary>
    public string? Motivo { get; private set; }

    public string? Ip { get; private set; }

    /// <summary>Texto livre curto para quem lê o relatório (ex.: "Maria Souza: nível 1 para 2"). Nunca guarda foto, vetor nem score.</summary>
    public string? Detalhe { get; private set; }

    private RegistroDeAcesso()
    {
    }

    public static RegistroDeAcesso Criar(DateTimeOffset agora, Guid? usuarioId, EventoDeAcesso evento, ResultadoDoAcesso resultado, string? motivo, string? ip, string? detalhe = null) => new()
    {
        OcorridoEm = agora,
        UsuarioId = usuarioId,
        Evento = evento,
        Resultado = resultado,
        Motivo = motivo,
        Ip = ip,
        Detalhe = detalhe,
    };
}
