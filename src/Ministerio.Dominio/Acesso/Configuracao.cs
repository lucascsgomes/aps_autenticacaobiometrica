namespace Ministerio.Dominio.Acesso;

/// <summary>Uma escolha do administrador guardada no banco (hoje: como o serviço facial prepara a foto).</summary>
public sealed class Configuracao
{
    public string Chave { get; private set; } = default!;
    public string Valor { get; private set; } = default!;
    public DateTimeOffset AtualizadoEm { get; private set; }

    private Configuracao()
    {
    }

    public static Configuracao Criar(string chave, string valor, DateTimeOffset agora) => new() { Chave = chave, Valor = valor, AtualizadoEm = agora };

    public void Definir(string valor, DateTimeOffset agora) => (Valor, AtualizadoEm) = (valor, agora);
}
