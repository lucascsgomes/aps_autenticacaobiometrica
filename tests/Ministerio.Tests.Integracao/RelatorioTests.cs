using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Ministerio.Contratos;

namespace Ministerio.Tests.Integracao;

/// <summary>O relatório de segurança: toda tentativa (aceita ou recusada) fica registrada, e só o ministro lê.</summary>
public class RelatorioTests : IClassFixture<FaceEnrollTests.SemAtalho>
{
    private readonly ApiFactory _fabrica;
    private readonly HttpClient _cliente;

    public RelatorioTests(FaceEnrollTests.SemAtalho semAtalho)
    {
        _fabrica = semAtalho.Fabrica;
        _fabrica.Facial.Falha = null;
        _fabrica.Facial.Corresponde = true;
        _fabrica.Facial.IdentificarComo = null;
        _cliente = _fabrica.CreateClient();
    }

    private static readonly SemaphoreSlim _trava = new(1, 1);
    private static Titular? _admin;
    private static Titular? _ministro;

    private async Task<Titular> MinistroAsync()
    {
        await _trava.WaitAsync();
        try
        {
            if (_ministro is null)
            {
                _admin ??= await _cliente.NovoTitularComRostoAsync(ApiFactory.CpfDoAdmin);
                _ministro = await _cliente.NovoTitularComRostoAsync();
                await _cliente.DefinirNivelAsync(_admin, _ministro, 3);
            }

            return _ministro;
        }
        finally
        {
            _trava.Release();
        }
    }

    private static async Task<string?> Code(HttpResponseMessage resposta)
    {
        using var doc = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    [Fact]
    public async Task Servidor_nao_le_o_relatorio_e_a_tentativa_aparece_nele()
    {
        var servidor = await _cliente.NovoTitularComRostoAsync();
        var ministro = await MinistroAsync();

        var recusado = await _cliente.Enviar(HttpMethod.Get, "/v1/relatorios/resumo", servidor.Sessao);
        var csv = await _cliente.Enviar(HttpMethod.Get, "/v1/relatorios/acessos.csv", servidor.Sessao);
        var resumo = await (await _cliente.Enviar(HttpMethod.Get, "/v1/relatorios/resumo", ministro.Sessao)).Ler<RelatorioResponse>();

        Assert.Equal(HttpStatusCode.Forbidden, recusado.StatusCode);
        Assert.Equal("NIVEL_INSUFICIENTE", await Code(recusado));
        Assert.Equal(HttpStatusCode.Forbidden, csv.StatusCode);
        Assert.Contains(resumo.Recentes, a => a.Evento == "RELATORIO" && a.Resultado == "RECUSADO" && a.Usuario == servidor.Sessao.Usuario.Nome);
    }

    [Fact]
    public async Task Senha_errada_e_nivel_acima_do_permitido_entram_nas_contagens()
    {
        var servidor = await _cliente.NovoTitularComRostoAsync();
        var ministro = await MinistroAsync();
        await _cliente.PostAsJsonAsync("/v1/auth/login/senha", new { cpf = servidor.Cpf, senha = "senha-errada-123" }, ClienteDeTeste.Json);
        await _cliente.Enviar(HttpMethod.Post, "/v1/biometria/desafios", servidor.Sessao, new { finalidade = "ACESSO_NIVEL_3" });

        var resumo = await (await _cliente.Enviar(HttpMethod.Get, "/v1/relatorios/resumo", ministro.Sessao)).Ler<RelatorioResponse>();

        Assert.True(resumo.Total > 0 && resumo.Recusados > 0 && resumo.TentativasIndevidas > 0);
        Assert.Equal(resumo.Total, resumo.Aceitos + resumo.Recusados);
        Assert.Contains(resumo.PorMotivo, c => c.Nome == "INVALID_CREDENTIALS");
        Assert.Contains(resumo.PorMotivo, c => c.Nome == "NIVEL_INSUFICIENTE");
        Assert.Contains(resumo.PorEvento, c => c.Nome == "LOGIN_SENHA");
        Assert.Contains(resumo.Recentes, a => a.Evento == "LOGIN_SENHA" && a.Motivo == "INVALID_CREDENTIALS" && a.Usuario == servidor.Sessao.Usuario.Nome);
        Assert.Contains(resumo.Recentes, a => a.Evento == "CONSULTA_NIVEL3" && a.Motivo == "NIVEL_INSUFICIENTE");
    }

    [Fact]
    public async Task Rosto_desconhecido_entra_no_relatorio_como_nao_identificado()
    {
        var ministro = await MinistroAsync();
        await _cliente.CadastrarComRostoAsync();

        await _cliente.EnviarFormularioAsync("/v1/auth/login/identificar", null, ClienteDeTeste.Foto);
        var resumo = await (await _cliente.Enviar(HttpMethod.Get, "/v1/relatorios/resumo", ministro.Sessao)).Ler<RelatorioResponse>();

        Assert.Contains(resumo.Recentes, a => a.Evento == "LOGIN_IDENTIFICACAO" && a.Resultado == "RECUSADO" && a.Usuario is null && a.Motivo == "FACE_NOT_VERIFIED");
    }

    [Fact]
    public async Task O_resumo_nunca_traz_foto_vetor_nem_score()
    {
        var ministro = await MinistroAsync();

        var texto = await (await _cliente.Enviar(HttpMethod.Get, "/v1/relatorios/resumo", ministro.Sessao)).Content.ReadAsStringAsync();

        Assert.DoesNotContain("score", texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("embedding", texto, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task O_csv_tem_cabecalho_acentos_certos_e_nao_deixa_nome_virar_formula()
    {
        var ministro = await MinistroAsync();
        var (_, cadastro) = await _cliente.CadastrarPendenteAsync(nome: "=SOMA(1;1) Maria");
        await _cliente.EnviarFormularioAsync("/v1/auth/face/enroll", cadastro.CadastroToken, ClienteDeTeste.Foto);
        await _cliente.PostAsJsonAsync("/v1/auth/login/senha", new { cpf = "00000000000", senha = "qualquer-senha-123" }, ClienteDeTeste.Json);

        var resposta = await _cliente.Enviar(HttpMethod.Get, "/v1/relatorios/acessos.csv", ministro.Sessao);
        var bytes = await resposta.Content.ReadAsByteArrayAsync();
        var texto = Encoding.UTF8.GetString(bytes);

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("text/csv", resposta.Content.Headers.ContentType?.MediaType);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]); // BOM: o Excel abre com os acentos certos
        Assert.Contains("quando;usuario;evento;resultado;motivo;ip", texto, StringComparison.Ordinal);
        Assert.DoesNotContain(";=SOMA", texto, StringComparison.Ordinal);
    }
}
