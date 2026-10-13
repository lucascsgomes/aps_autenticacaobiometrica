using System.Net;
using System.Text.Json;
using Banco.Contratos;
using Banco.Dominio;

namespace Banco.Tests.Integracao;

/// <summary>Confirmação da transferência com o rosto: desafio amarrado à operação, limiar de 0,450 e rosto errado gasta a intenção.</summary>
public class TransferenciaFacialTests : IClassFixture<FaceEnrollTests.SemAtalho>, IClassFixture<ApiFactory>
{
    private readonly ApiFactory _fabrica;
    private readonly ApiFactory _semRosto;
    private readonly HttpClient _cliente;

    public TransferenciaFacialTests(FaceEnrollTests.SemAtalho semAtalho, ApiFactory semRosto)
    {
        _fabrica = semAtalho.Fabrica;
        _fabrica.Facial.Falha = null;
        _fabrica.Facial.Corresponde = true;
        _semRosto = semRosto;
        _cliente = _fabrica.CreateClient();
    }

    private ClienteFacialFalso Facial => _fabrica.Facial;

    private static string NovaChave() => Guid.NewGuid().ToString();

    private async Task<Titular> ComRostoAsync()
    {
        var (cpf, _, sessao) = await _cliente.CadastrarComRostoAsync();
        var conta = await (await _cliente.Enviar(HttpMethod.Get, "/v1/contas/me", sessao)).Ler<ContaMeResponse>();
        return new Titular(cpf, sessao, conta);
    }

    private static async Task<string?> Code(HttpResponseMessage resposta)
    {
        using var doc = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static async Task<IntencaoResponse> IntencaoAsync(HttpClient cliente, Titular de, Titular para, long centavos)
    {
        var resposta = await cliente.Enviar(HttpMethod.Post, "/v1/transferencias/intencoes", de.Sessao, new { chave_destino = para.Cpf, valor_centavos = centavos });
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        return await resposta.Ler<IntencaoResponse>();
    }

    private async Task<DesafioResponse> DesafioAsync(Titular quem, Guid transferenciaId)
    {
        var resposta = await _cliente.Enviar(HttpMethod.Post, $"/v1/transferencias/{transferenciaId}/desafio", quem.Sessao);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        return await resposta.Ler<DesafioResponse>();
    }

    private Task<HttpResponseMessage> ConfirmarComRosto(Titular quem, Guid transferenciaId, Guid desafioId, string? chave = null, byte[]? imagem = null, bool semChave = false)
    {
        var requisicao = new HttpRequestMessage(HttpMethod.Post, $"/v1/transferencias/{transferenciaId}/confirmar-face");
        requisicao.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", quem.Sessao.AccessToken);
        if (!semChave)
        {
            requisicao.Headers.Add("Idempotency-Key", chave ?? NovaChave());
        }

        var corpo = new MultipartFormDataContent { { new StringContent(desafioId.ToString()), "desafio_id" } };
        if (imagem is not { Length: 0 })
        {
            var arquivo = new ByteArrayContent(imagem ?? ClienteDeTeste.Foto);
            arquivo.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            corpo.Add(arquivo, "image", "rosto.jpg");
        }

        requisicao.Content = corpo;
        return _cliente.SendAsync(requisicao);
    }

    private async Task<string> StatusAsync(Titular quem, Guid transferenciaId) =>
        (await (await _cliente.Enviar(HttpMethod.Get, $"/v1/transferencias/{transferenciaId}", quem.Sessao)).Ler<TransferenciaResponse>()).Status;

    private async Task<long> SaldoAsync(Titular quem) =>
        (await (await _cliente.Enviar(HttpMethod.Get, "/v1/contas/me", quem.Sessao)).Ler<ContaMeResponse>()).SaldoCentavos;

    [Fact]
    public async Task Rosto_que_confere_move_o_dinheiro_no_limiar_de_transferencia_e_o_comprovante_diz_FACE()
    {
        var (ana, bia) = (await ComRostoAsync(), await ComRostoAsync());
        var intencao = await IntencaoAsync(_cliente, ana, bia, 20_000);
        Assert.Equal(["FACE", "SENHA"], intencao.Metodos);
        var desafio = await DesafioAsync(ana, intencao.TransferenciaId);

        var resposta = await ConfirmarComRosto(ana, intencao.TransferenciaId, desafio.DesafioId);
        var comprovante = await resposta.Ler<ComprovanteResponse>();

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("FACE", comprovante.MetodoConfirmacao);
        Assert.Equal(0.450m, Facial.UltimoLimiar);
        Assert.Equal(ClienteFacialFalso.Vetor, Facial.UltimaReferencia);
        Assert.Equal(480_000, await SaldoAsync(ana));
        Assert.Equal(520_000, await SaldoAsync(bia));
        Assert.DoesNotContain("score", await resposta.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Rosto_recusado_cancela_a_transferencia_e_nao_move_dinheiro()
    {
        var (ana, bia) = (await ComRostoAsync(), await ComRostoAsync());
        var intencao = await IntencaoAsync(_cliente, ana, bia, 20_000);
        var desafio = await DesafioAsync(ana, intencao.TransferenciaId);
        Facial.Corresponde = false;

        var resposta = await ConfirmarComRosto(ana, intencao.TransferenciaId, desafio.DesafioId);

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
        Assert.Equal("FACE_NOT_VERIFIED", await Code(resposta));
        Assert.Equal("FALHA", await StatusAsync(ana, intencao.TransferenciaId));
        Assert.Equal(500_000, await SaldoAsync(ana));
    }

    [Theory]
    [InlineData("rosto")]
    [InlineData("servico")]
    public async Task Foto_sem_rosto_ou_servico_fora_do_ar_deixa_a_transferencia_pendente_e_a_proxima_foto_conclui(string tipo)
    {
        var (ana, bia) = (await ComRostoAsync(), await ComRostoAsync());
        var intencao = await IntencaoAsync(_cliente, ana, bia, 20_000);
        var desafio = await DesafioAsync(ana, intencao.TransferenciaId);
        Facial.Falha = tipo == "rosto" ? new RostoNaoDetectadoException() : new ServicoFacialIndisponivelException();

        var falhou = await ConfirmarComRosto(ana, intencao.TransferenciaId, desafio.DesafioId);
        Facial.Falha = null;

        Assert.Equal(tipo == "rosto" ? HttpStatusCode.UnprocessableEntity : HttpStatusCode.ServiceUnavailable, falhou.StatusCode);
        Assert.Equal("PENDENTE_BIOMETRIA", await StatusAsync(ana, intencao.TransferenciaId));
        Assert.Equal(HttpStatusCode.OK, (await ConfirmarComRosto(ana, intencao.TransferenciaId, desafio.DesafioId)).StatusCode);
        Assert.Equal(480_000, await SaldoAsync(ana));
    }

    [Fact]
    public async Task Desafio_de_outra_transferencia_ou_de_outro_usuario_nao_vale_e_nao_cancela_a_transferencia()
    {
        var (ana, bia, caio) = (await ComRostoAsync(), await ComRostoAsync(), await ComRostoAsync());
        var primeira = await IntencaoAsync(_cliente, ana, bia, 10_000);
        var desafioDaPrimeira = await DesafioAsync(ana, primeira.TransferenciaId);
        var segunda = await IntencaoAsync(_cliente, ana, bia, 10_000);
        var duasOutras = await IntencaoAsync(_cliente, caio, bia, 10_000);

        var trocado = await ConfirmarComRosto(ana, segunda.TransferenciaId, desafioDaPrimeira.DesafioId);
        var deOutroUsuario = await ConfirmarComRosto(caio, duasOutras.TransferenciaId, desafioDaPrimeira.DesafioId);
        var abrirDeOutro = await _cliente.Enviar(HttpMethod.Post, $"/v1/transferencias/{segunda.TransferenciaId}/desafio", caio.Sessao);

        Assert.Equal("FACE_NOT_VERIFIED", await Code(trocado));
        Assert.Equal("FACE_NOT_VERIFIED", await Code(deOutroUsuario));
        Assert.Equal(HttpStatusCode.NotFound, abrirDeOutro.StatusCode);
        Assert.Equal("PENDENTE_BIOMETRIA", await StatusAsync(ana, segunda.TransferenciaId));
    }

    [Fact]
    public async Task Abrir_o_desafio_de_novo_devolve_o_mesmo_e_o_desafio_vale_uma_vez()
    {
        var (ana, bia) = (await ComRostoAsync(), await ComRostoAsync());
        var intencao = await IntencaoAsync(_cliente, ana, bia, 5_000);

        var a = await DesafioAsync(ana, intencao.TransferenciaId);
        var b = await DesafioAsync(ana, intencao.TransferenciaId);
        Assert.Equal(a.DesafioId, b.DesafioId);
        Assert.Equal(HttpStatusCode.OK, (await ConfirmarComRosto(ana, intencao.TransferenciaId, a.DesafioId)).StatusCode);

        var depois = await ConfirmarComRosto(ana, intencao.TransferenciaId, a.DesafioId);
        Assert.Equal(HttpStatusCode.Conflict, depois.StatusCode);
        Assert.Equal("TRANSFER_NOT_PENDING", await Code(depois));
    }

    [Fact]
    public async Task Mesma_Idempotency_Key_repete_a_resposta_sem_nova_verificacao()
    {
        var (ana, bia) = (await ComRostoAsync(), await ComRostoAsync());
        var intencao = await IntencaoAsync(_cliente, ana, bia, 5_000);
        var desafio = await DesafioAsync(ana, intencao.TransferenciaId);
        var chave = NovaChave();
        var primeira = await ConfirmarComRosto(ana, intencao.TransferenciaId, desafio.DesafioId, chave);
        var verificacoes = Facial.Verificacoes;

        var repetida = await ConfirmarComRosto(ana, intencao.TransferenciaId, desafio.DesafioId, chave);

        Assert.Equal(HttpStatusCode.OK, repetida.StatusCode);
        Assert.Equal((await primeira.Ler<ComprovanteResponse>()).E2eId, (await repetida.Ler<ComprovanteResponse>()).E2eId);
        Assert.Equal(verificacoes, Facial.Verificacoes);
        Assert.Equal(495_000, await SaldoAsync(ana));
    }

    [Fact]
    public async Task Conta_sem_rosto_so_oferece_senha_e_nao_abre_desafio()
    {
        var cliente = _semRosto.CreateClient();
        var ana = await cliente.NovoTitularAsync();
        var bia = await cliente.NovoTitularAsync("Beatriz Souza Lima");
        var intencao = await IntencaoAsync(cliente, ana, bia, 5_000);

        var desafio = await cliente.Enviar(HttpMethod.Post, $"/v1/transferencias/{intencao.TransferenciaId}/desafio", ana.Sessao);

        Assert.Equal(["SENHA"], intencao.Metodos);
        Assert.Equal(HttpStatusCode.Unauthorized, desafio.StatusCode);
        Assert.Equal("FACE_NOT_VERIFIED", await Code(desafio));
    }

    [Fact]
    public async Task Pedido_mal_formado_e_recusado_antes_de_gastar_o_desafio()
    {
        var (ana, bia) = (await ComRostoAsync(), await ComRostoAsync());
        var intencao = await IntencaoAsync(_cliente, ana, bia, 5_000);
        var desafio = await DesafioAsync(ana, intencao.TransferenciaId);

        var semChave = await ConfirmarComRosto(ana, intencao.TransferenciaId, desafio.DesafioId, semChave: true);
        var semImagem = await ConfirmarComRosto(ana, intencao.TransferenciaId, desafio.DesafioId, imagem: []);

        Assert.Equal(HttpStatusCode.BadRequest, semChave.StatusCode);
        Assert.Equal("IDEMPOTENCY_KEY_REQUIRED", await Code(semChave));
        Assert.Equal(HttpStatusCode.BadRequest, semImagem.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ConfirmarComRosto(ana, intencao.TransferenciaId, desafio.DesafioId)).StatusCode);
    }
}
