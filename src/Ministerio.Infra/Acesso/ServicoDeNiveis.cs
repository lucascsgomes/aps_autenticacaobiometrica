using Ministerio.Dominio.Acesso;
using Microsoft.Extensions.Configuration;

namespace Ministerio.Infra.Acesso;

/// <summary>
/// Quem é ministro e quem é diretor vem da configuração (<c>Niveis:Ministro</c> e <c>Niveis:Diretores</c>, listas de CPF), nunca do cadastro:
/// ninguém vira ministro por conta própria. Quem não está em nenhuma lista é servidor (nível 1).
/// </summary>
public sealed class ServicoDeNiveis
{
    private readonly string? _ministro;
    private readonly HashSet<string> _diretores;

    public ServicoDeNiveis(IConfiguration configuracao)
    {
        _ministro = configuracao["Niveis:Ministro"];
        _diretores = (configuracao.GetSection("Niveis:Diretores").Get<string[]>() ?? []).ToHashSet();
    }

    public int De(string cpf) => cpf == _ministro ? Niveis.Ministro : _diretores.Contains(cpf) ? Niveis.Diretor : Niveis.Servidor;
}
