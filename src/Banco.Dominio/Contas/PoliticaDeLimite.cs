namespace Banco.Dominio.Contas;

/// <summary>
/// Limite diurno e noturno, no fuso America/Sao_Paulo. O diurno vale para o dia civil inteiro; o noturno
/// vale para a janela de <c>InicioNoturno</c> até <c>FimNoturno</c> do dia seguinte, e só é conferido enquanto ela está aberta.
/// </summary>
public static class PoliticaDeLimite
{
    private static readonly TimeZoneInfo Fuso = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    public sealed record Janelas(DateTimeOffset DiaInicio, DateTimeOffset DiaFim, (DateTimeOffset Inicio, DateTimeOffset Fim)? Noite);

    public static Janelas Calcular(Conta conta, DateTimeOffset agora)
    {
        var local = TimeZoneInfo.ConvertTime(agora, Fuso).DateTime;
        var dia = DateOnly.FromDateTime(local);
        var hora = TimeOnly.FromDateTime(local);

        (DateTimeOffset, DateTimeOffset)? noite = null;
        if (hora >= conta.InicioNoturno || hora < conta.FimNoturno)
        {
            var diaDeInicio = hora >= conta.InicioNoturno ? dia : dia.AddDays(-1);
            noite = (Utc(diaDeInicio, conta.InicioNoturno), Utc(diaDeInicio.AddDays(1), conta.FimNoturno));
        }

        return new Janelas(Utc(dia, TimeOnly.MinValue), Utc(dia.AddDays(1), TimeOnly.MinValue), noite);
    }

    /// <summary>Devolve o motivo da recusa, ou nulo se cabe nos dois limites. Quem chama soma as transferências já concluídas de cada janela.</summary>
    public static string? MotivoDaRecusa(Conta conta, Janelas janelas, long somaDoDia, long somaDaNoite, long valorCentavos)
    {
        if (janelas.Noite is not null && somaDaNoite + valorCentavos > conta.LimiteNoturnoCentavos)
        {
            return Transferencias.MotivoFalha.LimiteNoturno;
        }

        return somaDoDia + valorCentavos > conta.LimiteDiurnoCentavos ? Transferencias.MotivoFalha.LimiteDiurno : null;
    }

    private static DateTimeOffset Utc(DateOnly dia, TimeOnly hora)
    {
        var local = dia.ToDateTime(hora);
        return new DateTimeOffset(local, Fuso.GetUtcOffset(local)).ToUniversalTime();
    }
}
