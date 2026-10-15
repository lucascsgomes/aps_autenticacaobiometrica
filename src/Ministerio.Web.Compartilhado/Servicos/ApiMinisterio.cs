using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ministerio.Contratos;

namespace Ministerio.Web.Servicos;

public sealed record ProblemaApi(int Status, string? Code, string Mensagem);

/// <summary>Como a pessoa confirma uma ação sensível: a senha ou uma foto do rosto, nunca as duas.</summary>
public sealed record Credencial(string? Senha, byte[]? Imagem);

public sealed record Resposta<T>(T? Valor, ProblemaApi? Erro)
{
    public bool Ok => Erro is null;
}

/// <summary>Cliente da API: renova a sessão antes de vencer e traduz os erros (Problem Details) em mensagem para a tela.</summary>
public sealed class ApiMinisterio(HttpClient http, SessaoApp sessao) : IDisposable
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private readonly SemaphoreSlim _renovando = new(1, 1);

    public void Dispose() => _renovando.Dispose();

    // ---- cadastro e entrada ----

    public Task<Resposta<RegistrarResponse>> RegistrarAsync(RegistrarRequest dados) =>
        EnviarAsync<RegistrarResponse>(() => ComJson(HttpMethod.Post, "/v1/auth/register", dados), anonima: true);

    public Task<Resposta<RetomarResponse>> RetomarAsync(string cpf, string senha) =>
        EnviarAsync<RetomarResponse>(() => ComJson(HttpMethod.Post, "/v1/auth/register/retomar", new RetomarRequest(cpf, senha, "RETOMAR", null)), anonima: true);

    public async Task<Resposta<SessaoResponse>> CadastrarRostoAsync(string cadastroToken, byte[] imagem) =>
        await Abrir(await EnviarAsync<SessaoResponse>(() => ComFoto("/v1/auth/face/enroll", imagem), cadastroToken));

    public async Task<Resposta<SessaoResponse>> LoginComSenhaAsync(string cpf, string senha) =>
        await Abrir(await EnviarAsync<SessaoResponse>(() => ComJson(HttpMethod.Post, "/v1/auth/login/senha", new LoginSenhaRequest(cpf, senha)), anonima: true));

    public Task<Resposta<DesafioResponse>> DesafioDeLoginAsync(string cpf) =>
        EnviarAsync<DesafioResponse>(() => ComJson(HttpMethod.Post, "/v1/auth/login/desafio", new LoginDesafioRequest(cpf)), anonima: true);

    public async Task<Resposta<SessaoResponse>> LoginComRostoAsync(Guid desafioId, byte[] imagem) =>
        await Abrir(await EnviarAsync<SessaoResponse>(() => ComFoto("/v1/auth/login/face", imagem, ("desafio_id", desafioId.ToString())), anonima: true));

    public async Task SairAsync()
    {
        if (sessao.Atual is { } atual)
        {
            await EnviarAsync<bool>(() => ComJson(HttpMethod.Post, "/v1/auth/logout", new RefreshRequest(atual.RefreshToken)), anonima: true);
        }

        await sessao.EncerrarAsync();
    }

    /// <summary>Volta a sessão guardada ao abrir a página (ou renova a que está perto de vencer). Falso se não há sessão.</summary>
    public async Task<bool> RestaurarAsync()
    {
        await _renovando.WaitAsync();
        try
        {
            if (sessao.Ativa && !sessao.PrestesAVencer)
            {
                return true;
            }

            var refresh = sessao.Atual?.RefreshToken ?? await sessao.RefreshGuardadoAsync();
            if (string.IsNullOrEmpty(refresh))
            {
                return false;
            }

            var r = await EnviarAsync<SessaoResponse>(() => ComJson(HttpMethod.Post, "/v1/auth/refresh", new RefreshRequest(refresh)), anonima: true);
            if (r.Ok)
            {
                await sessao.DefinirAsync(r.Valor!);
            }
            else if (r.Erro!.Status is 400 or 401)
            {
                await sessao.EncerrarAsync();
            }

            return r.Ok;
        }
        finally
        {
            _renovando.Release();
        }
    }

    // ---- acesso ----

    public Task<Resposta<PerfilResponse>> PerfilAsync() => EnviarAsync<PerfilResponse>(() => new HttpRequestMessage(HttpMethod.Get, "/v1/me"));

    /// <summary>Entrada só pelo rosto, sem CPF: o serviço compara a foto com todos os cadastrados (1 para N).</summary>
    public async Task<Resposta<SessaoResponse>> LoginPorIdentificacaoAsync(byte[] imagem) =>
        await Abrir(await EnviarAsync<SessaoResponse>(() => ComFoto("/v1/auth/login/identificar", imagem), anonima: true));

    public Task<Resposta<DadosResponse>> DadosDoNivel1Async() => EnviarAsync<DadosResponse>(() => new HttpRequestMessage(HttpMethod.Get, "/v1/dados/1"));

    /// <summary>Níveis 2 e 3: abre o desafio de rosto do nível e manda a foto junto, numa chamada só do ponto de vista da tela.</summary>
    public Task<Resposta<DadosResponse>> DadosRestritosAsync(int nivel, byte[] imagem) =>
        AcaoSensivelAsync<DadosResponse>(HttpMethod.Post, $"/v1/dados/{nivel}", $"ACESSO_NIVEL_{nivel}", null, new Credencial(null, imagem));

    public Task<Resposta<List<UsuarioAdminDto>>> UsuariosAdminAsync() =>
        EnviarAsync<List<UsuarioAdminDto>>(() => new HttpRequestMessage(HttpMethod.Get, "/v1/admin/usuarios"));

    /// <summary>Muda o nível e/ou o perfil de admin de alguém. O rosto do admin confirma, amarrado a este usuário e a estes valores.</summary>
    public Task<Resposta<bool>> AlterarAcessoAsync(Guid usuarioId, int? nivel, bool? admin, byte[] imagem)
    {
        var pedido = new AlterarAcessoRequest(usuarioId, nivel, admin);
        return AcaoSensivelAsync<bool>(HttpMethod.Put, $"/v1/admin/usuarios/{usuarioId}/acesso", "ALTERAR_ACESSO", pedido, new Credencial(null, imagem), pedido);
    }

    public Task<Resposta<RelatorioResponse>> RelatorioAsync() => EnviarAsync<RelatorioResponse>(() => new HttpRequestMessage(HttpMethod.Get, "/v1/relatorios/resumo"));

    public Task<Resposta<byte[]>> BaixarRelatorioAsync() => EnviarAsync<byte[]>(() => new HttpRequestMessage(HttpMethod.Get, "/v1/relatorios/acessos.csv"));

    // ---- perfil ----

    public Task<Resposta<BiometriaResponse>> BiometriaAsync() => EnviarAsync<BiometriaResponse>(() => new HttpRequestMessage(HttpMethod.Get, "/v1/me/biometria"));

    public Task<Resposta<bool>> ExcluirRostoAsync(string senha) =>
        EnviarAsync<bool>(() => ComJson(HttpMethod.Delete, "/v1/me/biometria", new ConfirmarSenhaRequest(senha)));

    /// <summary>
    /// Ação que exige rosto ou senha: abre o desafio amarrado aos <paramref name="parametros"/> e repete a chamada em multipart
    /// com a credencial. <paramref name="dados"/> é o corpo da ação (os mesmos campos dos parâmetros).
    /// </summary>
    public async Task<Resposta<T>> AcaoSensivelAsync<T>(HttpMethod metodo, string rota, string finalidade, object? parametros, Credencial credencial, object? dados = null, byte[]? novaImagem = null)
    {
        var elemento = parametros is null ? (JsonElement?)null : JsonSerializer.SerializeToElement(parametros, Json);
        var desafio = await EnviarAsync<DesafioDeAcaoResponse>(() => ComJson(HttpMethod.Post, "/v1/biometria/desafios", new DesafioDeAcaoRequest(finalidade, elemento)));
        if (!desafio.Ok)
        {
            return new Resposta<T>(default, desafio.Erro);
        }

        return await EnviarAsync<T>(() =>
        {
            var corpo = new MultipartFormDataContent { { new StringContent(desafio.Valor!.DesafioId.ToString()), "desafio_id" } };
            if (dados is not null)
            {
                corpo.Add(new StringContent(JsonSerializer.Serialize(dados, Json)), "dados");
            }

            if (credencial.Senha is not null)
            {
                corpo.Add(new StringContent(credencial.Senha), "senha");
            }

            AdicionarFoto(corpo, credencial.Imagem, "image");
            AdicionarFoto(corpo, novaImagem, "nova_image");
            return new HttpRequestMessage(metodo, rota) { Content = corpo };
        });
    }

    private static void AdicionarFoto(MultipartFormDataContent corpo, byte[]? imagem, string campo)
    {
        if (imagem is null)
        {
            return;
        }

        var foto = new ByteArrayContent(imagem);
        foto.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        corpo.Add(foto, campo, "rosto.jpg");
    }

    // ---- infraestrutura ----

    public static HttpRequestMessage ComJson(HttpMethod metodo, string rota, object? corpo = null) =>
        new(metodo, rota) { Content = corpo is null ? null : JsonContent.Create(corpo, corpo.GetType(), null, Json) };

    public static HttpRequestMessage ComFoto(string rota, byte[] imagem, params (string Campo, string Valor)[] campos)
    {
        var foto = new ByteArrayContent(imagem);
        foto.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        var corpo = new MultipartFormDataContent { { foto, "image", "rosto.jpg" } };
        foreach (var (campo, valor) in campos)
        {
            corpo.Add(new StringContent(valor), campo);
        }

        return new HttpRequestMessage(HttpMethod.Post, rota) { Content = corpo };
    }

    /// <summary>
    /// Envia o pedido. Sem <paramref name="bearer"/> e sem <paramref name="anonima"/> usa a sessão aberta, renovada antes de vencer
    /// (renovar só no 401 gastaria a confirmação de quem errou a senha, que também responde 401).
    /// </summary>
    public async Task<Resposta<T>> EnviarAsync<T>(Func<HttpRequestMessage> criar, string? bearer = null, bool anonima = false)
    {
        // Com foto, a resposta nunca chega antes da animação de análise ser vista (a API local responde em milésimos).
        var comFoto = criar().Content is MultipartFormDataContent m && m.Any(p => p.Headers.ContentType?.MediaType == "image/jpeg");
        var minimo = comFoto ? Task.Delay(1800) : Task.CompletedTask;
        var resposta = await EnviarSemEsperaAsync<T>(criar, bearer, anonima);
        await minimo;
        return resposta;
    }

    private async Task<Resposta<T>> EnviarSemEsperaAsync<T>(Func<HttpRequestMessage> criar, string? bearer, bool anonima)
    {
        var token = bearer;
        if (bearer is null && !anonima)
        {
            if (!await RestaurarAsync())
            {
                return new Resposta<T>(default, new ProblemaApi(401, "UNAUTHORIZED", "Sua sessão acabou. Entre de novo."));
            }

            token = sessao.Atual!.AccessToken;
        }

        try
        {
            using var requisicao = criar();
            if (token is not null)
            {
                requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            using var resposta = await http.SendAsync(requisicao);
            return await LerAsync<T>(resposta);
        }
        catch (HttpRequestException)
        {
            return new Resposta<T>(default, new ProblemaApi(0, "NETWORK", "Não consegui falar com o servidor. Confira se a API está no ar."));
        }
    }

    private async Task<Resposta<SessaoResponse>> Abrir(Resposta<SessaoResponse> resposta)
    {
        if (resposta.Ok)
        {
            await sessao.DefinirAsync(resposta.Valor!);
        }

        return resposta;
    }

    private static async Task<Resposta<T>> LerAsync<T>(HttpResponseMessage resposta)
    {
        if (resposta.IsSuccessStatusCode)
        {
            if (typeof(T) == typeof(bool))
            {
                return new Resposta<T>((T)(object)true, null);
            }

            return typeof(T) == typeof(byte[])
                ? new Resposta<T>((T)(object)await resposta.Content.ReadAsByteArrayAsync(), null)
                : new Resposta<T>(await resposta.Content.ReadFromJsonAsync<T>(Json), null);
        }

        string? code = null;
        string? mensagem = null;
        try
        {
            using var doc = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
            var raiz = doc.RootElement;
            code = raiz.TryGetProperty("code", out var c) ? c.GetString() : null;
            mensagem = raiz.TryGetProperty("detail", out var d) ? d.GetString() : null;
            if (raiz.TryGetProperty("errors", out var erros) && erros.ValueKind == JsonValueKind.Object)
            {
                mensagem = string.Join(" ", erros.EnumerateObject().SelectMany(e => e.Value.EnumerateArray().Select(m => m.GetString())));
            }
        }
        catch (JsonException)
        {
            // corpo vazio ou que não é JSON: cai na mensagem pelo status
        }

        mensagem ??= resposta.StatusCode == System.Net.HttpStatusCode.TooManyRequests
            ? "Muitas tentativas seguidas. Espere um minuto e tente de novo."
            : "Não foi possível concluir. Tente de novo.";
        return new Resposta<T>(default, new ProblemaApi((int)resposta.StatusCode, code, mensagem));
    }
}
