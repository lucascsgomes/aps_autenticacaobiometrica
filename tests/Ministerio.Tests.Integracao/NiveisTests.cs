using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ministerio.Contratos;
using Ministerio.Dominio;

namespace Ministerio.Tests.Integracao;

/// <summary>
/// Os três níveis da base: o 1 abre com a sessão; o 2 e o 3 abrem só com o rosto confirmado na hora, e só para quem tem aquele nível.
/// Ministro e diretor são cadastros promovidos pelo admin (painel); qualquer outro cadastro é servidor.
/// </summary>
public class NiveisTests : IClassFixture<FaceEnrollTests.SemAtalho>
{
    private readonly ApiFactory _fabrica;
    private readonly HttpClient _cliente;

    public NiveisTests(FaceEnrollTests.SemAtalho semAtalho)
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

    // O admin (CPF da configuração) se cadastra uma vez por banco e promove os outros, como em produção.
    private static readonly SemaphoreSlim _trava = new(1, 1);
    private static Titular? _admin;
    private static Titular? _ministro;
    private static Titular? _diretor;

    private async Task<Titular> PromovidoAsync(int nivel)
    {
        await _trava.WaitAsync();
        try
        {
            _admin ??= await _cliente.NovoTitularComRostoAsync(ApiFactory.CpfDoAdmin);
            if (nivel == 3)
            {
                _ministro ??= await PromoverAsync(3);
                return _ministro;
            }

            _diretor ??= await PromoverAsync(2);
            return _diretor;
        }
        finally
        {
            _trava.Release();
        }
    }

    private async Task<Titular> PromoverAsync(int nivel)
    {
        var titular = await _cliente.NovoTitularComRostoAsync();
        await _cliente.DefinirNivelAsync(_admin!, titular, nivel);
        return titular;
    }

    private Task<Titular> MinistroAsync() => PromovidoAsync(3);

    private Task<Titular> DiretorAsync() => PromovidoAsync(2);

    private async Task<Guid> DesafioAsync(Titular quem, string finalidade)
    {
        var resposta = await _cliente.Enviar(HttpMethod.Post, "/v1/biometria/desafios", quem.Sessao, new { finalidade });
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        return (await resposta.Ler<DesafioDeAcaoResponse>()).DesafioId;
    }

    private Task<HttpResponseMessage> Abrir(Titular quem, int nivel, Guid? desafio, bool rosto = true, string? senha = null)
    {
        var requisicao = new HttpRequestMessage(HttpMethod.Post, $"/v1/dados/{nivel}");
        requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", quem.Sessao.AccessToken);
        var corpo = new MultipartFormDataContent();
        if (desafio is not null)
        {
            corpo.Add(new StringContent(desafio.Value.ToString()), "desafio_id");
        }

        if (senha is not null)
        {
            corpo.Add(new StringContent(senha), "senha");
        }

        if (rosto)
        {
            var arquivo = new ByteArrayContent(ClienteDeTeste.Foto);
            arquivo.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            corpo.Add(arquivo, "image", "rosto.jpg");
        }

        requisicao.Content = corpo;
        return _cliente.SendAsync(requisicao);
    }

    [Fact]
    public async Task O_perfil_mostra_o_nivel_que_vem_do_banco_e_a_entrada_ja_traz_o_nivel_novo()
    {
        var servidor = await _cliente.NovoTitularComRostoAsync();
        var ministro = await MinistroAsync();

        var doServidor = await (await _cliente.Enviar(HttpMethod.Get, "/v1/me", servidor.Sessao)).Ler<PerfilResponse>();
        var doMinistro = await (await _cliente.Enviar(HttpMethod.Get, "/v1/me", ministro.Sessao)).Ler<PerfilResponse>();

        Assert.Equal((1, "Servidor", true), (doServidor.Nivel, doServidor.Cargo, doServidor.TemRosto));
        Assert.Equal(3, doMinistro.Nivel);
        var entrada = await (await _cliente.PostAsJsonAsync("/v1/auth/login/senha", new { cpf = ministro.Cpf, senha = ClienteDeTeste.Senha }, ClienteDeTeste.Json)).Ler<SessaoResponse>();
        Assert.Equal(3, entrada.Usuario.Nivel);
        Assert.Equal("Ministro do Meio Ambiente", entrada.Usuario.Cargo);
        Assert.DoesNotContain(servidor.Cpf, doServidor.CpfMascarado, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nivel_1_abre_para_qualquer_servidor_e_nao_traz_campos_dos_outros_niveis()
    {
        var servidor = await _cliente.NovoTitularComRostoAsync();

        var resposta = await _cliente.Enviar(HttpMethod.Get, "/v1/dados/1", servidor.Sessao);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var dados = await resposta.Ler<DadosResponse>();
        Assert.Equal(20, dados.Itens.Count);
        Assert.All(dados.Itens, p =>
        {
            Assert.NotNull(p.Agrotoxico);
            Assert.Null(p.Propriedade);
            Assert.Null(p.Proprietario);
            Assert.Null(p.MultaCentavos);
        });
        Assert.Equal(HttpStatusCode.Unauthorized, (await _cliente.GetAsync("/v1/dados/1")).StatusCode);
    }

    [Fact]
    public async Task Servidor_nao_consegue_nem_abrir_o_desafio_do_nivel_2_e_a_tentativa_fica_registrada()
    {
        var servidor = await _cliente.NovoTitularComRostoAsync();
        var antes = Facial.Chamadas;

        var desafio = await _cliente.Enviar(HttpMethod.Post, "/v1/biometria/desafios", servidor.Sessao, new { finalidade = "ACESSO_NIVEL_2" });
        var direto = await Abrir(servidor, 2, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Forbidden, desafio.StatusCode);
        Assert.Equal("NIVEL_INSUFICIENTE", await Code(desafio));
        Assert.Equal(HttpStatusCode.Forbidden, direto.StatusCode);
        Assert.Equal("NIVEL_INSUFICIENTE", await Code(direto));
        Assert.Equal(antes, Facial.Chamadas); // recusou pelo nível antes de gastar o reconhecimento
    }

    [Fact]
    public async Task Diretor_abre_o_nivel_2_com_o_rosto_e_nao_ve_o_nivel_3()
    {
        var diretor = await DiretorAsync();
        var desafio = await DesafioAsync(diretor, "ACESSO_NIVEL_2");

        var resposta = await Abrir(diretor, 2, desafio);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal(0.400m, Facial.UltimoLimiar);
        var dados = await resposta.Ler<DadosResponse>();
        Assert.All(dados.Itens, p =>
        {
            Assert.NotNull(p.Propriedade);
            Assert.NotNull(p.QuantidadeLitros);
            Assert.Null(p.Proprietario);
            Assert.Null(p.Documento);
            Assert.Null(p.MultaCentavos);
        });

        var nivel3 = await _cliente.Enviar(HttpMethod.Post, "/v1/biometria/desafios", diretor.Sessao, new { finalidade = "ACESSO_NIVEL_3" });
        Assert.Equal(HttpStatusCode.Forbidden, nivel3.StatusCode);
        Assert.Equal("NIVEL_INSUFICIENTE", await Code(nivel3));
    }

    [Fact]
    public async Task Ministro_abre_o_nivel_3_com_limiar_mais_rigido_e_ve_tudo()
    {
        var ministro = await MinistroAsync();
        var desafio = await DesafioAsync(ministro, "ACESSO_NIVEL_3");

        var resposta = await Abrir(ministro, 3, desafio);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal(0.500m, Facial.UltimoLimiar);
        var dados = await resposta.Ler<DadosResponse>();
        Assert.All(dados.Itens, p =>
        {
            Assert.NotNull(p.Proprietario);
            Assert.NotNull(p.Documento);
            Assert.True(p.MultaCentavos > 0);
            Assert.NotNull(p.Situacao);
        });
    }

    [Fact]
    public async Task Cada_abertura_gasta_o_desafio_e_ele_nao_serve_para_outro_nivel()
    {
        var ministro = await MinistroAsync();
        var doNivel2 = await DesafioAsync(ministro, "ACESSO_NIVEL_2");
        var doNivel3 = await DesafioAsync(ministro, "ACESSO_NIVEL_3");

        var trocado = await Abrir(ministro, 3, doNivel2);
        var certo = await Abrir(ministro, 3, doNivel3);
        var repetido = await Abrir(ministro, 3, doNivel3);

        Assert.Equal(HttpStatusCode.Unauthorized, trocado.StatusCode);
        Assert.Equal("FACE_NOT_VERIFIED", await Code(trocado));
        Assert.Equal(HttpStatusCode.OK, certo.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, repetido.StatusCode);
    }

    [Fact]
    public async Task Os_niveis_restritos_nao_abrem_com_senha()
    {
        var diretor = await DiretorAsync();
        var desafio = await DesafioAsync(diretor, "ACESSO_NIVEL_2");

        var resposta = await Abrir(diretor, 2, desafio, rosto: false, senha: ClienteDeTeste.Senha);

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
        Assert.Equal("FACE_REQUIRED", await Code(resposta));
        Assert.Equal(HttpStatusCode.OK, (await Abrir(diretor, 2, desafio)).StatusCode); // o desafio não foi gasto
    }

    [Fact]
    public async Task Rosto_que_nao_bate_nao_abre_nada_e_gasta_o_desafio()
    {
        var diretor = await DiretorAsync();
        var desafio = await DesafioAsync(diretor, "ACESSO_NIVEL_2");

        Facial.Corresponde = false;
        var recusado = await Abrir(diretor, 2, desafio);
        Facial.Corresponde = true;
        var repetido = await Abrir(diretor, 2, desafio);

        Assert.Equal(HttpStatusCode.Unauthorized, recusado.StatusCode);
        Assert.Equal("FACE_NOT_VERIFIED", await Code(recusado));
        Assert.Equal(HttpStatusCode.Unauthorized, repetido.StatusCode);
    }

    [Fact]
    public async Task Foto_sem_rosto_devolve_o_desafio_para_tirar_outra()
    {
        var diretor = await DiretorAsync();
        var desafio = await DesafioAsync(diretor, "ACESSO_NIVEL_2");

        Facial.Falha = new RostoNaoDetectadoException();
        var semRosto = await Abrir(diretor, 2, desafio);
        Facial.Falha = null;
        var denovo = await Abrir(diretor, 2, desafio);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, semRosto.StatusCode);
        Assert.Equal("NO_FACE_DETECTED", await Code(semRosto));
        Assert.Equal(HttpStatusCode.OK, denovo.StatusCode);
    }

    [Fact]
    public async Task Sem_desafio_pede_a_confirmacao_e_desafio_de_outra_pessoa_nao_vale()
    {
        var diretor = await DiretorAsync();
        var ministro = await MinistroAsync();

        var sem = await Abrir(diretor, 2, desafio: null);
        var alheio = await Abrir(diretor, 2, await DesafioAsync(ministro, "ACESSO_NIVEL_2"));

        Assert.Equal(HttpStatusCode.Forbidden, sem.StatusCode);
        Assert.Equal("STEP_UP_REQUIRED", await Code(sem));
        Assert.Equal(HttpStatusCode.Unauthorized, alheio.StatusCode);
        Assert.Equal("FACE_NOT_VERIFIED", await Code(alheio));
    }

    [Fact]
    public async Task Pedir_um_nivel_que_nao_existe_ou_uma_finalidade_invalida_e_recusado()
    {
        var diretor = await DiretorAsync();

        var nivel4 = await Abrir(diretor, 4, Guid.NewGuid());
        var finalidade = await _cliente.Enviar(HttpMethod.Post, "/v1/biometria/desafios", diretor.Sessao, new { finalidade = "TRANSFERENCIA" });

        Assert.Equal(HttpStatusCode.NotFound, nivel4.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, finalidade.StatusCode);
    }
}
