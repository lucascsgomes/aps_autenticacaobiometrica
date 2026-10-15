using Microsoft.Extensions.Configuration;

namespace Ministerio.Infra.Acesso;

/// <summary>
/// Os primeiros administradores vêm da configuração (<c>Admin:Cpfs</c>, lista de CPFs): são admin sempre e ninguém tira,
/// senão um painel sem admin não teria como começar nem como se recuperar. Os demais admins são feitos pelo painel e ficam no banco.
/// </summary>
public sealed class ServicoDeAdmins
{
    private readonly HashSet<string> _fixos;

    public ServicoDeAdmins(IConfiguration configuracao) =>
        _fixos = (configuracao.GetSection("Admin:Cpfs").Get<string[]>() ?? []).Where(c => !string.IsNullOrWhiteSpace(c)).ToHashSet();

    /// <summary>Admin definido na configuração do servidor: o painel não consegue tirar.</summary>
    public bool Fixo(string cpf) => _fixos.Contains(cpf);

    public bool EhAdmin(string cpf, bool adminNoBanco) => adminNoBanco || Fixo(cpf);
}
