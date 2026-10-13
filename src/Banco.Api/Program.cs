using Banco.Api.Endpoints;
using System.Text.Json;
using Banco.Api.Middleware;
using Banco.Api.Seguranca;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Banco.Infra;
using Banco.Infra.Persistencia;

var builder = WebApplication.CreateBuilder(args);

// O contrato público usa snake_case no JSON.
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower);

var jwtOpcoes = JwtOpcoes.Carregar(builder.Configuration, builder.Environment);
builder.Services.AddSingleton(jwtOpcoes);
builder.Services.AddSingleton<JwtEmissor>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new JwtEmissor(jwtOpcoes, TimeProvider.System).ParametrosDeValidacao();
    });
builder.Services.AddAuthorization();

builder.Services.AdicionarLimites(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<TratadorGlobalDeExcecoes>();
builder.Services.AdicionarInfra(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddCheck<BancoHealthCheck>("banco", tags: [SaudeEndpoints.TagProntidao]);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapearSaude();
app.MapearAuth();
app.MapearContas();
app.MapearTransferencias();

app.Run();

// Exposto para o WebApplicationFactory dos testes de integração.
public partial class Program;
