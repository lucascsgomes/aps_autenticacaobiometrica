using Banco.Api.Endpoints;
using Banco.Api.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<TratadorGlobalDeExcecoes>();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.MapearSaude();

app.Run();

// Exposto para o WebApplicationFactory dos testes de integração.
public partial class Program;
