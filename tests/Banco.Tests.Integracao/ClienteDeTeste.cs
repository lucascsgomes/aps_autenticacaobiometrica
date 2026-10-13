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

    /// <summary>Cadastro de produção: o usuário nasce pendente e a resposta traz o cadastro_token.</summary>
    public static async Task<(string Cpf, RegistrarResponse Cadastro)> CadastrarPendenteAsync(this HttpClient cliente, string nome = "Ana Beatriz Moreira", string? email = null)
    {
        var n = Interlocked.Increment(ref _sequencia);
        var cpf = AuthTests.CpfValido(n);
        var resposta = await cliente.PostAsJsonAsync("/v1/auth/register", new
        {
            nome,
            cpf,
            email = email ?? $"rosto{n}@exemplo.com",
            senha = Senha,
            consentimento_biometrico = new { aceito = true, versao_termo = "2026-10-v1" },
        }, Json);
        Assert.Equal(System.Net.HttpStatusCode.Created, resposta.StatusCode);
        return (cpf, await resposta.Ler<RegistrarResponse>());
    }

    public static readonly byte[] Foto = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4];

    /// <summary>Formulário multipart com a foto no campo dado e mais os campos de texto.</summary>
    public static Task<HttpResponseMessage> EnviarFormularioAsync(this HttpClient cliente, string rota, string? bearer, byte[]? imagem, string campoDaImagem = "image", params (string Campo, string Valor)[] campos)
    {
        var requisicao = new HttpRequestMessage(HttpMethod.Post, rota);
        if (bearer is not null)
        {
            requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        var corpo = new MultipartFormDataContent();
        foreach (var (campo, valor) in campos)
        {
            corpo.Add(new StringContent(valor), campo);
        }

        if (imagem is not null)
        {
            var arquivo = new ByteArrayContent(imagem);
            arquivo.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            corpo.Add(arquivo, campoDaImagem, "rosto.jpg");
        }

        requisicao.Content = corpo;
        return cliente.SendAsync(requisicao);
    }

    /// <summary>Cadastro completo com rosto (o face-service é o falso da fábrica). Devolve CPF e a sessão do cadastro.</summary>
    public static async Task<(string Cpf, Guid UsuarioId, SessaoResponse Sessao)> CadastrarComRostoAsync(this HttpClient cliente)
    {
        var (cpf, cadastro) = await cliente.CadastrarPendenteAsync();
        var resposta = await cliente.EnviarFormularioAsync("/v1/auth/face/enroll", cadastro.CadastroToken, Foto);
        Assert.Equal(System.Net.HttpStatusCode.Created, resposta.StatusCode);
        return (cpf, cadastro.UsuarioId, await resposta.Ler<SessaoResponse>());
    }
}
