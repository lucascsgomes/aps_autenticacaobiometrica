namespace Banco.Tests.Integracao;

/// <summary>Relógio dos testes: anda com o real, pode ser adiantado ou fixado numa hora de Brasília.</summary>
internal sealed class RelogioAjustavel : TimeProvider
{
    private static readonly TimeZoneInfo Brasilia = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    private TimeSpan _deslocamento;
    private DateTimeOffset? _fixo;

    public void Avancar(TimeSpan quanto) => _deslocamento += quanto;

    public void Fixar(DateTimeOffset instante) => _fixo = instante;

    public override DateTimeOffset GetUtcNow() => _fixo ?? System.GetUtcNow() + _deslocamento;

    /// <summary>Hoje (dia de Brasília) às <paramref name="hora"/> horas.</summary>
    public static DateTimeOffset HojeAs(int hora)
    {
        var hoje = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Brasilia).Date.AddHours(hora);
        return new DateTimeOffset(hoje, Brasilia.GetUtcOffset(hoje)).ToUniversalTime();
    }
}
