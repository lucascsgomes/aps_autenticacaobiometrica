using Ministerio.Web;
using Ministerio.Web.Servicos;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
builder.Services.AdicionarMinisterioWeb(new Uri(builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5080"));
await builder.Build().RunAsync();
