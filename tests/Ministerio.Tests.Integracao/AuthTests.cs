using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ministerio.Contratos;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Ministerio.Tests.Integracao;

/// <summary>Cadastro, login por senha, refresh e proteção das rotas, com a API inteira e PostgreSQL real.</summary>
public class AuthTests(ApiFactory fabrica) : IClassFixture<ApiFactory>
{
    private const string Senha = "uma-senha-longa-e-unica";
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private static int _sequencia;

    private readonly HttpClient _cliente = fabrica.CreateClient();

    private sealed record Usuario(string Cpf, string Email);

    private static Usuario NovoUsuario()
    {
        var n = Interlocked.Increment(ref _sequencia);
        return new Usuario(CpfValido(n), $"pessoa{n}@exemplo.com");
    }

    /// <summary>Gera um CPF com os dois dígitos verificadores corretos a partir de um número de 9 dígitos.</summary>
    internal static string CpfValido(int n)
    {
        var digitos = (100_000_000 + n).ToString(CultureInfo.InvariantCulture).Select(c => c - '0').ToList();
        for (var tamanho = 9; tamanho <= 10; tamanho++)
        {
            var soma = Enumerable.Range(0, tamanho).Sum(i => digitos[i] * (tamanho + 1 - i));
            var resto = soma * 10 % 11;
            digitos.Add(resto == 10 ? 0 : resto);
        }

        return string.Concat(digitos);
    }

    private static object CorpoDeCadastro(Usuario u, string? senha = Senha, bool aceito = true) => new
    {
        nome = "Ana Beatriz Moreira",
        cpf = u.Cpf,
        email = u.Email,
        celular = "+5511987654321",
        senha,
        consentimento_biometrico = new { aceito, versao_termo = "2026-10-v1" },
    };

    private Task<HttpResponseMessage> Registrar(Usuario u, string? senha = Senha, bool aceito = true, HttpClient? cliente = null) =>
        (cliente ?? _cliente).PostAsJsonAsync("/v1/auth/register", CorpoDeCadastro(u, senha, aceito), Json);

    private Task<HttpResponseMessage> Login(string cpf, string senha) =>
        _cliente.PostAsJsonAsync("/v1/auth/login/senha", new { cpf, senha }, Json);

    private static async Task<T> Ler<T>(HttpResponseMessage resposta) =>
        (await resposta.Content.ReadFromJsonAsync<T>(Json))!;

    private async Task<SessaoResponse> RegistrarEEntrar(Usuario? u = null)
    {
        u ??= NovoUsuario();
        var resposta = await Registrar(u);
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        return (await Ler<RegistrarResponse>(resposta)).Sessao!;
    }

    private Task<HttpResponseMessage> ContaMe(string? token)
    {
        var requisicao = new HttpRequestMessage(HttpMethod.Get, "/v1/me");
        if (token is not null)
        {
            requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return _cliente.SendAsync(requisicao);
    }

    private static async Task<string?> Code(HttpResponseMessage resposta)
    {
        using var doc = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    [Fact]
    public async Task Cadastro_sem_rosto_ativa_o_cadastro_e_ja_entra()
    {
        var resposta = await Registrar(NovoUsuario());
        var corpo = await Ler<RegistrarResponse>(resposta);

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        Assert.NotNull(corpo.Sessao);
        Assert.Null(corpo.ProximoPasso);

        var perfil = await ContaMe(corpo.Sessao.AccessToken);
        var dados = await Ler<PerfilResponse>(perfil);
        Assert.Equal(HttpStatusCode.OK, perfil.StatusCode);
        Assert.Equal("Ana Beatriz Moreira", dados.Nome);
        Assert.Equal(corpo.UsuarioId, dados.Id);
        Assert.Equal(1, dados.Nivel);
    }

    [Fact]
    public async Task Cadastro_com_dados_invalidos_devolve_400_com_o_campo_errado()
    {
        var u = NovoUsuario();

        var cpfComPontos = await _cliente.PostAsJsonAsync("/v1/auth/register", CorpoDeCadastro(u with { Cpf = "529.982.247-25" }), Json);
        var senhaCurta = await Registrar(u, senha: "curta");
        var semConsentimento = await Registrar(u, aceito: false);

        foreach (var (resposta, campo) in new[] { (cpfComPontos, "cpf"), (senhaCurta, "senha"), (semConsentimento, "consentimento_biometrico") })
        {
            Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
            Assert.Equal("VALIDATION_ERROR", await Code(resposta));
            Assert.Contains($"\"{campo}\"", await resposta.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Cpf_ou_email_repetido_devolve_409_generico()
    {
        var u = NovoUsuario();
        await Registrar(u);

        var mesmoCpf = await Registrar(u with { Email = "outro@exemplo.com" });
        var mesmoEmail = await Registrar(NovoUsuario() with { Email = u.Email });

        Assert.Equal(HttpStatusCode.Conflict, mesmoCpf.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, mesmoEmail.StatusCode);
        Assert.Equal("REGISTRATION_CONFLICT", await Code(mesmoCpf));
        Assert.Equal(await Code(mesmoCpf), await Code(mesmoEmail));
    }

    [Fact]
    public async Task Cadastro_sem_o_modo_de_desenvolvimento_fica_pendente_e_nao_entra()
    {
        await using var producao = new ApiFactory { CadastroSemRosto = false };
        var cliente = producao.CreateClient();
        var u = NovoUsuario();

        var resposta = await Registrar(u, cliente: cliente);
        var corpo = await Ler<RegistrarResponse>(resposta);
        var login = await cliente.PostAsJsonAsync("/v1/auth/login/senha", new { cpf = u.Cpf, senha = Senha }, Json);

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        Assert.Null(corpo.Sessao);
        Assert.Equal("CAPTURA_FACIAL", corpo.ProximoPasso);
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        Assert.Equal("INVALID_CREDENTIALS", await Code(login));
    }

    [Fact]
    public async Task Login_com_senha_correta_devolve_sessao_com_metodo_senha()
    {
        var u = NovoUsuario();
        await Registrar(u);

        var resposta = await Login(u.Cpf, Senha);
        var sessao = await Ler<SessaoResponse>(resposta);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("SENHA", sessao.Metodo);
        Assert.Equal("Bearer", sessao.TokenType);
        Assert.Equal(900, sessao.ExpiresIn);
        Assert.Equal(HttpStatusCode.OK, (await ContaMe(sessao.AccessToken)).StatusCode);
    }

    [Fact]
    public async Task Cpf_inexistente_e_senha_errada_respondem_exatamente_igual()
    {
        var u = NovoUsuario();
        await Registrar(u);

        var senhaErrada = await Login(u.Cpf, "senha-errada-qualquer");
        var cpfInexistente = await Login(CpfValido(900_000), "senha-errada-qualquer");

        Assert.Equal(HttpStatusCode.Unauthorized, senhaErrada.StatusCode);
        Assert.Equal(senhaErrada.StatusCode, cpfInexistente.StatusCode);
        Assert.Equal("INVALID_CREDENTIALS", await Code(senhaErrada));
        Assert.Equal(await Code(senhaErrada), await Code(cpfInexistente));
    }

    [Fact]
    public async Task Cinco_senhas_erradas_bloqueiam_e_o_bloqueio_so_aparece_com_a_senha_certa()
    {
        var u = NovoUsuario();
        await Registrar(u);

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await Login(u.Cpf, "senha-errada-qualquer")).StatusCode);
        }

        var aindaErrada = await Login(u.Cpf, "senha-errada-qualquer");
        var certa = await Login(u.Cpf, Senha);

        Assert.Equal(HttpStatusCode.Unauthorized, aindaErrada.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, certa.StatusCode);
        Assert.Equal("ACCOUNT_LOCKED", await Code(certa));
        Assert.True(certa.Headers.TryGetValues("Retry-After", out var segundos) && int.Parse(segundos.Single(), CultureInfo.InvariantCulture) is > 0 and <= 60);
    }

    [Fact]
    public async Task Refresh_troca_o_token_e_o_antigo_deixa_de_valer_revogando_a_familia()
    {
        var sessao = await RegistrarEEntrar();

        var primeira = await _cliente.PostAsJsonAsync("/v1/auth/refresh", new { refresh_token = sessao.RefreshToken }, Json);
        var nova = await Ler<SessaoResponse>(primeira);
        var reuso = await _cliente.PostAsJsonAsync("/v1/auth/refresh", new { refresh_token = sessao.RefreshToken }, Json);
        var daFamiliaRevogada = await _cliente.PostAsJsonAsync("/v1/auth/refresh", new { refresh_token = nova.RefreshToken }, Json);

        Assert.Equal(HttpStatusCode.OK, primeira.StatusCode);
        Assert.NotEqual(sessao.RefreshToken, nova.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, reuso.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, daFamiliaRevogada.StatusCode);
    }

    [Fact]
    public async Task Logout_revoga_a_familia_e_e_idempotente()
    {
        var sessao = await RegistrarEEntrar();

        var sair = await _cliente.PostAsJsonAsync("/v1/auth/logout", new { refresh_token = sessao.RefreshToken }, Json);
        var sairDeNovo = await _cliente.PostAsJsonAsync("/v1/auth/logout", new { refresh_token = sessao.RefreshToken }, Json);
        var renovar = await _cliente.PostAsJsonAsync("/v1/auth/refresh", new { refresh_token = sessao.RefreshToken }, Json);

        Assert.Equal(HttpStatusCode.NoContent, sair.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, sairDeNovo.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, renovar.StatusCode);
    }

    [Fact]
    public async Task Refresh_com_token_desconhecido_ou_vazio_devolve_401()
    {
        var desconhecido = await _cliente.PostAsJsonAsync("/v1/auth/refresh", new { refresh_token = "nao-existe" }, Json);
        var vazio = await _cliente.PostAsJsonAsync("/v1/auth/refresh", new { }, Json);

        Assert.Equal(HttpStatusCode.Unauthorized, desconhecido.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, vazio.StatusCode);
        Assert.Equal("UNAUTHORIZED", await Code(desconhecido));
    }

    [Fact]
    public async Task Cada_usuario_ve_so_o_proprio_perfil()
    {
        var a = await RegistrarEEntrar();
        var b = await RegistrarEEntrar();

        var perfilDeA = await Ler<PerfilResponse>(await ContaMe(a.AccessToken));
        var perfilDeB = await Ler<PerfilResponse>(await ContaMe(b.AccessToken));

        Assert.NotEqual(perfilDeA.Id, perfilDeB.Id);
        Assert.Equal(a.Usuario.Id, perfilDeA.Id);
        Assert.Equal(b.Usuario.Id, perfilDeB.Id);
    }

    [Fact]
    public async Task Rota_protegida_recusa_ausente_adulterado_alg_none_audiencia_errada_e_expirado()
    {
        var valido = (await RegistrarEEntrar()).AccessToken;
        var partes = valido.Split('.');
        var adulterado = $"{partes[0]}.{partes[1]}.{new string(partes[2].Reverse().ToArray())}";
        var algNone = $"{Base64(@"{""alg"":""none"",""typ"":""JWT""}")}.{partes[1]}.";

        var casos = new Dictionary<string, string?>
        {
            ["ausente"] = null,
            ["assinatura adulterada"] = adulterado,
            ["alg none"] = algNone,
            ["audiencia errada"] = Assinar(audiencia: "outro-sistema"),
            ["emissor errado"] = Assinar(emissor: "outro-emissor"),
            ["expirado"] = Assinar(expira: DateTime.UtcNow.AddMinutes(-5)),
            ["outra chave"] = Assinar(chave: "outra-chave-bem-longa-com-mais-de-trinta-e-dois-bytes"),
        };

        foreach (var (nome, token) in casos)
        {
            Assert.True(HttpStatusCode.Unauthorized == (await ContaMe(token)).StatusCode, $"Caso '{nome}' deveria dar 401.");
        }

        Assert.Equal(HttpStatusCode.OK, (await ContaMe(valido)).StatusCode);
    }

    private static string Base64(string texto) => Base64UrlEncoder.Encode(texto);

    private static string Assinar(string audiencia = "ministerio-clientes", string emissor = "ministerio-api", DateTime? expira = null, string chave = ApiFactory.ChaveJwt) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = emissor,
            Audience = audiencia,
            NotBefore = DateTime.UtcNow.AddHours(-1),
            Expires = expira ?? DateTime.UtcNow.AddMinutes(10),
            Claims = new Dictionary<string, object> { ["sub"] = Guid.NewGuid().ToString(), ["esc"] = "sessao" },
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(chave)), SecurityAlgorithms.HmacSha256),
        });
}
