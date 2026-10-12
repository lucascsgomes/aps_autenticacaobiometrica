using Banco.Api.Endpoints;
using Banco.Api.Middleware;
using Banco.Infra;
using Banco.Infra.Persistencia;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<TratadorGlobalDeExcecoes>();
builder.Services.AdicionarInfra(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddCheck<BancoHealthCheck>("banco", tags: [SaudeEndpoints.TagProntidao]);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.MapearSaude();

app.Run();

// Exposto para o WebApplicationFactory dos testes de integração.
public partial class Program;
