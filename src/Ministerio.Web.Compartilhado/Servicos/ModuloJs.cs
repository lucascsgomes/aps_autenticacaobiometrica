using Microsoft.JSInterop;

namespace Ministerio.Web.Servicos;

/// <summary>Carrega o <c>ponte.js</c> uma vez só e entrega o módulo a quem precisar.</summary>
public sealed class ModuloJs(IJSRuntime js)
{
    private IJSObjectReference? _modulo;

    public async ValueTask<IJSObjectReference> ObterAsync() =>
        _modulo ??= await js.InvokeAsync<IJSObjectReference>("import", "./_content/Ministerio.Web.Compartilhado/ponte.js");
}
