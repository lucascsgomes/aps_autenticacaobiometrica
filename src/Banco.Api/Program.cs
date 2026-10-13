using Banco.Api.Endpoints;
using System.Text.Json;
using Banco.Api.Middleware;
using Banco.Api.Seguranca;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Banco.Infra;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
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
builder.Services.AddAuthorization(o =>
{
    // Rota autenticada sem política explícita exige token de sessão: o token de cadastro só vale para enviar o rosto.
    o.DefaultPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().RequireClaim("esc", JwtOpcoes.EscopoSessao).Build();
    o.AddPolicy(JwtOpcoes.PoliticaDeCadastro, p => p.RequireAuthenticatedUser().RequireClaim("esc", JwtOpcoes.EscopoCadastro));
});
builder.Services.Configure<FormOptions>(o => o.MemoryBufferThreshold = 3 * 1024 * 1024); // a foto nunca vai para disco

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
app.MapearStepUp();
app.MapearCredenciais();
app.MapearTransferencias();

app.Run();

// Exposto para o WebApplicationFactory dos testes de integração.
public partial class Program;
