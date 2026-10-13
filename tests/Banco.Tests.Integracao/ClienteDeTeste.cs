using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Banco.Contratos;

namespace Banco.Tests.Integracao;

internal sealed record Titular(string Cpf, SessaoResponse Sessao, ContaMeResponse Conta);

/// <summary>Cadastro e chamadas autenticadas repetidas pelos testes de integração.</summary>
internal static class ClienteDeTeste
{
    public const string Senha = "uma-senha-longa-e-unica";
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private static int _sequencia = 1000;

    /// <summary>Cadastra pelo modo sem rosto: já nasce ativo, com R$ 5.000 e a chave CPF.</summary>
    public static async Task<Titular> NovoTitularAsync(this HttpClient cliente, string nome = "Ana Beatriz Moreira")
    {
        var n = Interlocked.Increment(ref _sequencia);
        var cpf = AuthTests.CpfValido(n);
        var resposta = await cliente.PostAsJsonAsync("/v1/auth/register", new
        {
            nome,
            cpf,
            email = $"conta{n}@exemplo.com",
            senha = Senha,
            consentimento_biometrico = new { aceito = true, versao_termo = "2026-10-v1" },
        }, Json);
        var sessao = (await resposta.Ler<RegistrarResponse>()).Sessao!;
        var conta = await (await cliente.Enviar(HttpMethod.Get, "/v1/contas/me", sessao)).Ler<ContaMeResponse>();
        return new Titular(cpf, sessao, conta);
    }

    public static async Task<T> Ler<T>(this HttpResponseMessage resposta) => (await resposta.Content.ReadFromJsonAsync<T>(Json))!;

    public static Task<HttpResponseMessage> Enviar(this HttpClient cliente, HttpMethod metodo, string rota, SessaoResponse? sessao, object? corpo = null, string? chaveDeIdempotencia = null)
    {
        var requisicao = new HttpRequestMessage(metodo, rota);
        if (sessao is not null)
        {
            requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sessao.AccessToken);
        }

        if (chaveDeIdempotencia is not null)
        {
            requisicao.Headers.Add("Idempotency-Key", chaveDeIdempotencia);
        }

        if (corpo is not null)
        {
            requisicao.Content = JsonContent.Create(corpo, options: Json);
        }

        return cliente.SendAsync(requisicao);
    }
}
