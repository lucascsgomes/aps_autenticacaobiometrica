using Ministerio.Web.Servicos;
using Microsoft.AspNetCore.Components;

namespace Ministerio.Web.Componentes;

/// <summary>Base das páginas que só existem com sessão aberta: sem sessão (nem guardada), manda para a entrada.</summary>
public abstract class PaginaProtegida : ComponentBase
{
    [Inject]
    protected ApiMinisterio Api { get; set; } = default!;

    [Inject]
    protected NavigationManager Navegacao { get; set; } = default!;

    protected bool Autenticado { get; private set; }

    protected override async Task OnInitializedAsync()
    {
        if (!await Api.RestaurarAsync())
        {
            Navegacao.NavigateTo("entrar");
            return;
        }

        Autenticado = true;
        await CarregarAsync();
    }

    protected virtual Task CarregarAsync() => Task.CompletedTask;
}
