using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Banco.Contratos;
using Banco.Dominio;

namespace Banco.Tests.Integracao;

/// <summary>Consultar, trocar e excluir o rosto, e trocar a senha: tudo com step-up e sessões encerradas depois da troca.</summary>
public class CredenciaisTests : IClassFixture<FaceEnrollTests.SemAtalho>
{
    private const string NovaSenha = "outra-senha-longa-e-nova";

    private readonly ApiFactory _fabrica;
    private readonly HttpClient _cliente;

    public CredenciaisTests(FaceEnrollTests.SemAtalho semAtalho)
    {
        _fabrica = semAtalho.Fabrica;
        _fabrica.Facial.Falha = null;
        _fabrica.Facial.Corresponde = true;
        _cliente = _fabrica.CreateClient();
    }

    private ClienteFacialFalso Facial => _fabrica.Facial;

    private static async Task<string?> Code(HttpResponseMessage resposta)
    {
        using var doc = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private async Task<Guid> DesafioAsync(Titular quem, string finalidade)
    {
        var resposta = await _cliente.Enviar(HttpMethod.Post, "/v1/biometria/desafios", quem.Sessao, new { finalidade });
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        return (await resposta.Ler<DesafioDeAcaoResponse>()).DesafioId;
    }

    private Task<HttpResponseMessage> Multipart(HttpMethod metodo, string rota, Titular quem, Guid? desafioId, string? senha, bool rostoAtual = false, bool novaFoto = false, object? dados = null)
    {
        var requisicao = new HttpRequestMessage(metodo, rota);
        requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", quem.Sessao.AccessToken);
        var corpo = new MultipartFormDataContent();
        if (dados is not null)
        {
            corpo.Add(new StringContent(JsonSerializer.Serialize(dados, ClienteDeTeste.Json)), "dados");
        }

        if (desafioId is not null)
        {
            corpo.Add(new StringContent(desafioId.Value.ToString()), "desafio_id");
        }

        if (senha is not null)
        {
            corpo.Add(new StringContent(senha), "senha");
        }

        Adicionar(corpo, rostoAtual, "image");
        Adicionar(corpo, novaFoto, "nova_image");
        requisicao.Content = corpo;
        return _cliente.SendAsync(requisicao);
    }

    private static void Adicionar(MultipartFormDataContent corpo, bool incluir, string campo)
    {
        if (!incluir)
        {
            return;
        }

        var arquivo = new ByteArrayContent(ClienteDeTeste.Foto);
        arquivo.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        corpo.Add(arquivo, campo, "rosto.jpg");
    }

    private Task<HttpResponseMessage> TrocarRosto(Titular quem, Guid? desafio, string? senha = ClienteDeTeste.Senha, bool rostoAtual = false) =>
        Multipart(HttpMethod.Put, "/v1/me/biometria", quem, desafio, senha, rostoAtual, novaFoto: true);

    private Task<HttpResponseMessage> TrocarSenha(Titular quem, Guid? desafio, string? senha = ClienteDeTeste.Senha, string novaSenha = NovaSenha) =>
        Multipart(HttpMethod.Put, "/v1/me/senha", quem, desafio, senha, dados: new { nova_senha = novaSenha });

    private async Task<BiometriaResponse> StatusAsync(Titular quem) =>
        await (await _cliente.Enviar(HttpMethod.Get, "/v1/me/biometria", quem.Sessao)).Ler<BiometriaResponse>();

    private async Task<HttpResponseMessage> RenovarAsync(Titular quem) =>
        await _cliente.PostAsJsonAsync("/v1/auth/refresh", new { refresh_token = quem.Sessao.RefreshToken }, ClienteDeTeste.Json);

    [Fact]
    public async Task Consulta_mostra_que_o_rosto_esta_cadastrado()
    {
        var ana = await _cliente.NovoTitularComRostoAsync();

        var status = await StatusAsync(ana);

        Assert.True(status.Cadastrada);
        Assert.NotNull(status.CadastradaEm);
        Assert.Equal("sface-2021dec", status.Modelo);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _cliente.GetAsync("/v1/me/biometria")).StatusCode);
    }

    [Fact]
    public async Task Trocar_o_rosto_com_a_senha_guarda_o_novo_e_encerra_as_sessoes()
    {
        var ana = await _cliente.NovoTitularComRostoAsync();
        var antes = (await StatusAsync(ana)).CadastradaEm;
        var desafio = await DesafioAsync(ana, "RECADASTRO");

        var resposta = await TrocarRosto(ana, desafio);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.True((await resposta.Ler<BiometriaResponse>()).Cadastrada);
        Assert.True((await StatusAsync(ana)).CadastradaEm >= antes);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RenovarAsync(ana)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await TrocarRosto(ana, desafio)).StatusCode); // desafio de uso único
    }

    [Fact]
    public async Task Trocar_o_rosto_com_o_rosto_atual_usa_o_limiar_de_acao_sensivel()
    {
        var ana = await _cliente.NovoTitularComRostoAsync();
        var desafio = await DesafioAsync(ana, "RECADASTRO");

        var resposta = await TrocarRosto(ana, desafio, senha: null, rostoAtual: true);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal(0.450m, Facial.UltimoLimiar);
    }

    [Fact]
    public async Task Foto_nova_sem_rosto_nao_gasta_o_desafio()
    {
        var ana = await _cliente.NovoTitularComRostoAsync();
        var desafio = await DesafioAsync(ana, "RECADASTRO");

        Facial.Falha = new RostoNaoDetectadoException();
        var semRosto = await TrocarRosto(ana, desafio);
        Facial.Falha = null;
        var denovo = await TrocarRosto(ana, desafio);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, semRosto.StatusCode);
        Assert.Equal("NO_FACE_DETECTED", await Code(semRosto));
        Assert.Equal(HttpStatusCode.OK, denovo.StatusCode);
    }

    [Fact]
    public async Task Senha_errada_na_troca_do_rosto_recusa_e_mantem_o_rosto_antigo()
    {
        var ana = await _cliente.NovoTitularComRostoAsync();
        var antes = (await StatusAsync(ana)).CadastradaEm;
        var desafio = await DesafioAsync(ana, "RECADASTRO");

        var resposta = await TrocarRosto(ana, desafio, senha: "senha-errada-123");

        Assert.Equal("PASSWORD_NOT_VERIFIED", await Code(resposta));
        Assert.Equal(antes, (await StatusAsync(ana)).CadastradaEm);
        Assert.Equal(HttpStatusCode.OK, (await RenovarAsync(ana)).StatusCode); // sessão intacta
    }

    [Fact]
    public async Task Sem_desafio_as_duas_trocas_pedem_confirmacao()
    {
        var ana = await _cliente.NovoTitularComRostoAsync();

        var rosto = await TrocarRosto(ana, desafio: null);
        var senha = await TrocarSenha(ana, desafio: null);

        Assert.Equal(HttpStatusCode.Forbidden, rosto.StatusCode);
        Assert.Equal("STEP_UP_REQUIRED", await Code(rosto));
        Assert.Equal(HttpStatusCode.Forbidden, senha.StatusCode);
        Assert.Equal("STEP_UP_REQUIRED", await Code(senha));
    }

    [Fact]
    public async Task Desafio_de_outra_finalidade_nao_serve_para_trocar_a_senha()
    {
        var ana = await _cliente.NovoTitularComRostoAsync();
        var desafioDoRosto = await DesafioAsync(ana, "RECADASTRO");

        var resposta = await TrocarSenha(ana, desafioDoRosto);

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _cliente.PostAsJsonAsync("/v1/auth/login/senha", new { cpf = ana.Cpf, senha = ClienteDeTeste.Senha }, ClienteDeTeste.Json)).StatusCode);
    }

    [Fact]
    public async Task Excluir_o_rosto_exige_a_senha_e_so_vale_se_ele_existe()
    {
        var ana = await _cliente.NovoTitularComRostoAsync();

        var errada = await _cliente.Enviar(HttpMethod.Delete, "/v1/me/biometria", ana.Sessao, new { senha = "senha-errada-123" });
        var sem = await _cliente.Enviar(HttpMethod.Delete, "/v1/me/biometria", ana.Sessao, new { });
        Assert.Equal("PASSWORD_NOT_VERIFIED", await Code(errada));
        Assert.Equal(HttpStatusCode.BadRequest, sem.StatusCode);
        Assert.True((await StatusAsync(ana)).Cadastrada);

        var certa = await _cliente.Enviar(HttpMethod.Delete, "/v1/me/biometria", ana.Sessao, new { senha = ClienteDeTeste.Senha });
        var denovo = await _cliente.Enviar(HttpMethod.Delete, "/v1/me/biometria", ana.Sessao, new { senha = ClienteDeTeste.Senha });

        Assert.Equal(HttpStatusCode.NoContent, certa.StatusCode);
        Assert.False((await StatusAsync(ana)).Cadastrada);
        Assert.Equal(HttpStatusCode.Conflict, denovo.StatusCode);
        Assert.Equal("BIOMETRIA_NOT_REGISTERED", await Code(denovo));
    }

    [Fact]
    public async Task Depois_de_excluir_da_para_cadastrar_o_rosto_de_novo()
    {
        var ana = await _cliente.NovoTitularComRostoAsync();
        await _cliente.Enviar(HttpMethod.Delete, "/v1/me/biometria", ana.Sessao, new { senha = ClienteDeTeste.Senha });

        var resposta = await TrocarRosto(ana, await DesafioAsync(ana, "RECADASTRO"));

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.True((await StatusAsync(ana)).Cadastrada);
    }

    [Fact]
    public async Task Trocar_a_senha_vale_para_o_login_e_encerra_as_sessoes()
    {
        var ana = await _cliente.NovoTitularComRostoAsync();
        var desafio = await DesafioAsync(ana, "TROCAR_SENHA");

        var resposta = await TrocarSenha(ana, desafio);

        Assert.Equal(HttpStatusCode.NoContent, resposta.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await RenovarAsync(ana)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _cliente.PostAsJsonAsync("/v1/auth/login/senha", new { cpf = ana.Cpf, senha = ClienteDeTeste.Senha }, ClienteDeTeste.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _cliente.PostAsJsonAsync("/v1/auth/login/senha", new { cpf = ana.Cpf, senha = NovaSenha }, ClienteDeTeste.Json)).StatusCode);
    }

    [Fact]
    public async Task Trocar_a_senha_com_o_rosto_funciona_e_senha_nova_curta_e_recusada()
    {
        var ana = await _cliente.NovoTitularComRostoAsync();

        var curta = await TrocarSenha(ana, await DesafioAsync(ana, "TROCAR_SENHA"), novaSenha: "curta");
        var comRosto = await Multipart(HttpMethod.Put, "/v1/me/senha", ana, await DesafioAsync(ana, "TROCAR_SENHA"), senha: null, rostoAtual: true, dados: new { nova_senha = NovaSenha });

        Assert.Equal(HttpStatusCode.BadRequest, curta.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, comRosto.StatusCode);
        Assert.Equal(0.450m, Facial.UltimoLimiar);
    }

    [Fact]
    public async Task Senha_atual_errada_nao_troca_a_senha()
    {
        var ana = await _cliente.NovoTitularComRostoAsync();

        var resposta = await TrocarSenha(ana, await DesafioAsync(ana, "TROCAR_SENHA"), senha: "senha-errada-123");

        Assert.Equal("PASSWORD_NOT_VERIFIED", await Code(resposta));
        Assert.Equal(HttpStatusCode.OK, (await _cliente.PostAsJsonAsync("/v1/auth/login/senha", new { cpf = ana.Cpf, senha = ClienteDeTeste.Senha }, ClienteDeTeste.Json)).StatusCode);
    }
}
