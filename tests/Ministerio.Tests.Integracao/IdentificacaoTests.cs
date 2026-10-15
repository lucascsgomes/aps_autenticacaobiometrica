using System.Net;
using System.Text.Json;
using Ministerio.Contratos;
using Ministerio.Dominio;

namespace Ministerio.Tests.Integracao;

/// <summary>Entrada só pelo rosto, sem CPF: o serviço facial compara a foto com todos os cadastrados (1 para N).</summary>
public class IdentificacaoTests : IClassFixture<FaceEnrollTests.SemAtalho>
{
    private const string Rota = "/v1/auth/login/identificar";

    private readonly ApiFactory _fabrica;
    private readonly HttpClient _cliente;

    public IdentificacaoTests(FaceEnrollTests.SemAtalho semAtalho)
    {
        _fabrica = semAtalho.Fabrica;
        _fabrica.Facial.Falha = null;
        _fabrica.Facial.Corresponde = true;
        _fabrica.Facial.IdentificarComo = null;
        _fabrica.Facial.Ambiguo = false;
        _cliente = _fabrica.CreateClient();
    }

    private ClienteFacialFalso Facial => _fabrica.Facial;

    private static async Task<string?> Code(HttpResponseMessage resposta)
    {
        using var doc = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private Task<HttpResponseMessage> Identificar() => _cliente.EnviarFormularioAsync(Rota, null, ClienteDeTeste.Foto);

    [Fact]
    public async Task Identifica_quem_e_abre_a_sessao_sem_pedir_cpf()
    {
        var (_, usuarioId, _) = await _cliente.CadastrarComRostoAsync();
        Facial.IdentificarComo = usuarioId.ToString();

        var resposta = await Identificar();

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var sessao = await resposta.Ler<SessaoResponse>();
        Assert.Equal(usuarioId, sessao.Usuario.Id);
        Assert.Equal("IDENTIFICACAO", sessao.Metodo);
        Assert.Equal(1, sessao.Usuario.Nivel);
        Assert.Equal(HttpStatusCode.OK, (await _cliente.Enviar(HttpMethod.Get, "/v1/me", sessao)).StatusCode);
    }

    [Fact]
    public async Task Manda_todos_os_cadastrados_com_o_limiar_de_login_e_a_margem()
    {
        var (_, primeiro, _) = await _cliente.CadastrarComRostoAsync();
        var (_, segundo, _) = await _cliente.CadastrarComRostoAsync();
        Facial.IdentificarComo = segundo.ToString();

        await Identificar();

        var ids = Facial.UltimosCandidatos!.Select(c => c.Id).ToList();
        Assert.Contains(primeiro.ToString(), ids);
        Assert.Contains(segundo.ToString(), ids);
        Assert.All(Facial.UltimosCandidatos!, c => Assert.Equal(ClienteFacialFalso.Vetor, c.Referencia)); // decifrados, e só aqui
        Assert.Equal(0.363m, Facial.UltimoLimiar);
        Assert.Equal(0.05m, Facial.UltimaMargem);
    }

    [Fact]
    public async Task Ninguem_identificado_responde_o_mesmo_erro_de_rosto_errado()
    {
        await _cliente.CadastrarComRostoAsync();

        var resposta = await Identificar();

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
        Assert.Equal("FACE_NOT_VERIFIED", await Code(resposta));
    }

    [Fact]
    public async Task Dois_candidatos_parecidos_demais_nao_entram()
    {
        var (_, usuarioId, _) = await _cliente.CadastrarComRostoAsync();
        Facial.IdentificarComo = usuarioId.ToString();
        Facial.Ambiguo = true;

        var resposta = await Identificar();

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
        Assert.Equal("FACE_NOT_VERIFIED", await Code(resposta));
    }

    [Fact]
    public async Task Foto_sem_rosto_e_servico_fora_do_ar_tem_os_mesmos_erros_das_outras_rotas()
    {
        await _cliente.CadastrarComRostoAsync();

        Facial.Falha = new RostoNaoDetectadoException();
        var semRosto = await Identificar();
        Facial.Falha = new ServicoFacialIndisponivelException();
        var fora = await Identificar();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, semRosto.StatusCode);
        Assert.Equal("NO_FACE_DETECTED", await Code(semRosto));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, fora.StatusCode);
    }

    [Fact]
    public async Task Sem_ninguem_cadastrado_o_servico_facial_nem_e_chamado()
    {
        await using var vazia = new ApiFactory { CadastroSemRosto = false };
        var antes = vazia.Facial.Identificacoes;

        var resposta = await vazia.CreateClient().EnviarFormularioAsync(Rota, null, ClienteDeTeste.Foto);

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
        Assert.Equal(antes, vazia.Facial.Identificacoes);
    }

    [Fact]
    public async Task A_foto_e_obrigatoria()
    {
        var semFoto = await _cliente.EnviarFormularioAsync(Rota, null, null);
        Assert.Equal(HttpStatusCode.BadRequest, semFoto.StatusCode);
        Assert.Equal("INVALID_IMAGE", await Code(semFoto));
    }
}
