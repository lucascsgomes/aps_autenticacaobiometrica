using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ministerio.Contratos;

namespace Ministerio.Tests.Integracao;

/// <summary>Quem parou o cadastro no rosto escolhe entre retomar de onde parou e recomeçar com dados novos.</summary>
public class RetomarCadastroTests : IClassFixture<FaceEnrollTests.SemAtalho>
{
    private readonly ApiFactory _fabrica;
    private readonly HttpClient _cliente;

    public RetomarCadastroTests(FaceEnrollTests.SemAtalho semAtalho)
    {
        _fabrica = semAtalho.Fabrica;
        _fabrica.Facial.Falha = null;
        _cliente = _fabrica.CreateClient();
    }

    private static async Task<string?> Code(HttpResponseMessage resposta)
    {
        using var doc = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private Task<HttpResponseMessage> Retomar(object corpo) => _cliente.PostAsJsonAsync("/v1/auth/register/retomar", corpo, ClienteDeTeste.Json);

    private static object Dados(string email, string senha = "outra-senha-longa-e-nova") => new
    {
        nome = "Ana Beatriz Moreira da Silva",
        email,
        celular = "+5511911112222",
        senha,
        consentimento_biometrico = new { aceito = true, versao_termo = "2026-11-v2" },
    };

    [Fact]
    public async Task Retomar_devolve_um_token_novo_que_conclui_o_cadastro_com_o_rosto()
    {
        var (cpf, original) = await _cliente.CadastrarPendenteAsync();

        var resposta = await Retomar(new { cpf, senha = ClienteDeTeste.Senha, acao = "RETOMAR" });
        var retomado = await resposta.Ler<RetomarResponse>();

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("CAPTURA_FACIAL", retomado.ProximoPasso);
        Assert.NotEqual(original.CadastroToken, retomado.CadastroToken);
        var enroll = await _cliente.EnviarFormularioAsync("/v1/auth/face/enroll", retomado.CadastroToken, ClienteDeTeste.Foto);
        Assert.Equal(HttpStatusCode.Created, enroll.StatusCode);
    }

    [Fact]
    public async Task Recomecar_troca_os_dados_e_a_senha_do_mesmo_usuario()
    {
        var (cpf, original) = await _cliente.CadastrarPendenteAsync();

        var resposta = await Retomar(new { cpf, senha = ClienteDeTeste.Senha, acao = "RECOMECAR", dados = Dados("novo-email-1@exemplo.com") });
        var recomecado = await resposta.Ler<RegistrarResponse>();

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        Assert.Equal(original.UsuarioId, recomecado.UsuarioId);
        Assert.Equal("CAPTURA_FACIAL", recomecado.ProximoPasso);
        Assert.NotNull(recomecado.CadastroToken);

        var sessao = await (await _cliente.EnviarFormularioAsync("/v1/auth/face/enroll", recomecado.CadastroToken, ClienteDeTeste.Foto)).Ler<SessaoResponse>();
        Assert.Equal("Ana Beatriz Moreira da Silva", sessao.Usuario.Nome);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _cliente.PostAsJsonAsync("/v1/auth/login/senha", new { cpf, senha = ClienteDeTeste.Senha }, ClienteDeTeste.Json)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _cliente.PostAsJsonAsync("/v1/auth/login/senha", new { cpf, senha = "outra-senha-longa-e-nova" }, ClienteDeTeste.Json)).StatusCode);
    }

    [Fact]
    public async Task Recomecar_com_email_de_outro_cadastro_da_409_e_nao_muda_nada()
    {
        var (cpf, _) = await _cliente.CadastrarPendenteAsync();
        await _cliente.CadastrarPendenteAsync(email: "dono-do-email@exemplo.com");

        var resposta = await Retomar(new { cpf, senha = ClienteDeTeste.Senha, acao = "RECOMECAR", dados = Dados("dono-do-email@exemplo.com") });

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal("REGISTRATION_CONFLICT", await Code(resposta));
        Assert.Equal(HttpStatusCode.OK, (await Retomar(new { cpf, senha = ClienteDeTeste.Senha, acao = "RETOMAR" })).StatusCode); // a senha antiga continua valendo
    }

    [Fact]
    public async Task Senha_errada_cpf_inexistente_e_cadastro_ja_concluido_com_senha_errada_respondem_igual()
    {
        var (cpf, _) = await _cliente.CadastrarPendenteAsync();
        var (concluidoCpf, _, _) = await _cliente.CadastrarComRostoAsync();

        var errada = await Retomar(new { cpf, senha = "senha-errada-123", acao = "RETOMAR" });
        var inexistente = await Retomar(new { cpf = "52998224725", senha = ClienteDeTeste.Senha, acao = "RETOMAR" });
        var concluidoSenhaErrada = await Retomar(new { cpf = concluidoCpf, senha = "senha-errada-123", acao = "RETOMAR" });

        foreach (var r in new[] { errada, inexistente, concluidoSenhaErrada })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
            Assert.Equal("INVALID_CREDENTIALS", await Code(r));
        }
    }

    [Fact]
    public async Task Cadastro_concluido_com_a_senha_certa_recebe_409_e_nao_ganha_token()
    {
        var (cpf, _, _) = await _cliente.CadastrarComRostoAsync();

        var resposta = await Retomar(new { cpf, senha = ClienteDeTeste.Senha, acao = "RETOMAR" });

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal("REGISTRATION_CONFLICT", await Code(resposta));
    }

    [Fact]
    public async Task Cinco_senhas_erradas_bloqueiam_a_retomada_e_a_senha_certa_recebe_403()
    {
        var (cpf, _) = await _cliente.CadastrarPendenteAsync();
        for (var i = 0; i < 5; i++)
        {
            await Retomar(new { cpf, senha = "senha-errada-123", acao = "RETOMAR" });
        }

        var certa = await Retomar(new { cpf, senha = ClienteDeTeste.Senha, acao = "RETOMAR" });

        Assert.Equal(HttpStatusCode.Forbidden, certa.StatusCode);
        Assert.Equal("ACCOUNT_LOCKED", await Code(certa));
    }

    [Theory]
    [InlineData("""{"cpf":"52998224725","senha":"x","acao":"TALVEZ"}""", "acao")]
    [InlineData("""{"cpf":"52998224725","senha":"x","acao":"RECOMECAR"}""", "dados")]
    [InlineData("""{"cpf":"52998224725","senha":"x","acao":"RECOMECAR","dados":{"nome":"Ana Souza","email":"a@b.com","senha":"curta","consentimento_biometrico":{"aceito":true,"versao_termo":"v1"}}}""", "dados.senha")]
    [InlineData("""{"cpf":"abc","senha":"x","acao":"RETOMAR"}""", "cpf")]
    public async Task Pedido_invalido_devolve_400_com_o_campo(string json, string campo)
    {
        var resposta = await _cliente.PostAsync("/v1/auth/register/retomar", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Contains(campo, await resposta.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
