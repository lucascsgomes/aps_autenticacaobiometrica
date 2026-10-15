using Ministerio.Contratos;
using Microsoft.JSInterop;

namespace Ministerio.Web.Servicos;

/// <summary>
/// A sessão aberta no navegador. O access token fica só na memória; o refresh token vai para o armazenamento local
/// para a pessoa continuar logada ao recarregar a página.
/// </summary>
public sealed class SessaoApp(ModuloJs modulo)
{
    private const string ChaveDoRefresh = "ministerio.refresh";

    public SessaoResponse? Atual { get; private set; }

    public bool Ativa => Atual is not null;

    /// <summary>Falta menos de 30 segundos para o access token vencer.</summary>
    public bool PrestesAVencer => Atual is null || DateTimeOffset.UtcNow >= _venceEm.AddSeconds(-30);

    private DateTimeOffset _venceEm;

    /// <summary>Recado para a próxima tela de entrada (ex.: "Senha trocada, entre de novo"). A tela lê e limpa.</summary>
    public string? Aviso { get; set; }

    public event Action? Mudou;

    public async Task DefinirAsync(SessaoResponse sessao)
    {
        Atual = sessao;
        _venceEm = DateTimeOffset.UtcNow.AddSeconds(sessao.ExpiresIn);
        await (await modulo.ObterAsync()).InvokeVoidAsync("guardar", ChaveDoRefresh, sessao.RefreshToken);
        Mudou?.Invoke();
    }

    public async Task EncerrarAsync()
    {
        Atual = null;
        await (await modulo.ObterAsync()).InvokeVoidAsync("remover", ChaveDoRefresh);
        Mudou?.Invoke();
    }

    public async Task<string?> RefreshGuardadoAsync() =>
        await (await modulo.ObterAsync()).InvokeAsync<string?>("ler", ChaveDoRefresh);
}
