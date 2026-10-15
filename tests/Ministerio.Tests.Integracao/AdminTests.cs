using System.Net;
using System.Text.Json;
using Ministerio.Contratos;
using Ministerio.Dominio;

namespace Ministerio.Tests.Integracao;

/// <summary>
/// O painel de acesso: só admin vê e muda. Toda mudança exige o rosto do admin, amarrado ao usuário e aos valores pedidos,
/// e fica no relatório. O admin da configuração (Admin:Cpfs) nunca sai pelo painel.
/// </summary>
public class AdminTests : IClassFixture<FaceEnrollTests.SemAtalho>
{
    private readonly ApiFactory _fabrica;
    private readonly HttpClient _cliente;

    public AdminTests(FaceEnrollTests.SemAtalho semAtalho)
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

    // O CPF fixo só se cadastra uma vez por banco, e os testes da classe dividem o mesmo banco.
    private static readonly SemaphoreSlim _trava = new(1, 1);
    private static Titular? _admin;

    private async Task<Titular> AdminAsync()
    {
        await _trava.WaitAsync();
        try
        {
            return _admin ??= await _cliente.NovoTitularComRostoAsync(ApiFactory.CpfDoAdmin);
        }
        finally
        {
            _trava.Release();
        }
    }

    private async Task<PerfilResponse> PerfilAsync(Titular quem) =>
        await (await _cliente.Enviar(HttpMethod.Get, "/v1/me", quem.Sessao)).Ler<PerfilResponse>();

    [Fact]
    public async Task O_admin_da_configuracao_ja_nasce_admin_e_o_resto_nasce_servidor()
    {
        var admin = await AdminAsync();
        var servidor = await _cliente.NovoTitularComRostoAsync();

        var doAdmin = await PerfilAsync(admin);
        var doServidor = await PerfilAsync(servidor);

        Assert.True(doAdmin.Admin);
        Assert.True(admin.Sessao.Usuario.Admin);
        Assert.Equal(1, doAdmin.Nivel);
        Assert.False(doServidor.Admin);
        Assert.Equal(1, doServidor.Nivel);
    }

    [Fact]
    public async Task So_admin_lista_os_usuarios()
    {
        var admin = await AdminAsync();
        var servidor = await _cliente.NovoTitularComRostoAsync();

        var lista = await _cliente.Enviar(HttpMethod.Get, "/v1/admin/usuarios", admin.Sessao);
        var recusada = await _cliente.Enviar(HttpMethod.Get, "/v1/admin/usuarios", servidor.Sessao);

        Assert.Equal(HttpStatusCode.OK, lista.StatusCode);
        var usuarios = await lista.Ler<List<UsuarioAdminDto>>();
        var eu = usuarios.Single(u => u.Id == admin.Sessao.Usuario.Id);
        Assert.True(eu.EhVoce && eu.Admin && eu.AdminFixo && eu.TemRosto);
        Assert.Contains(usuarios, u => u.Id == servidor.Sessao.Usuario.Id && !u.Admin && u.Nivel == 1 && !u.EhVoce);
        Assert.All(usuarios, u => Assert.DoesNotContain(servidor.Cpf, u.CpfMascarado, StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.Forbidden, recusada.StatusCode);
        Assert.Equal("ADMIN_REQUIRED", await Code(recusada));
        Assert.Equal(HttpStatusCode.Unauthorized, (await _cliente.GetAsync("/v1/admin/usuarios")).StatusCode);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Admin_muda_o_nivel_com_o_rosto_e_vale_na_hora_sem_novo_login(int nivel)
    {
        var admin = await AdminAsync();
        var alvo = await _cliente.NovoTitularComRostoAsync();

        var resposta = await _cliente.AlterarAcessoAsync(admin, alvo.Sessao.Usuario.Id, nivel);

        Assert.Equal(HttpStatusCode.NoContent, resposta.StatusCode);
        Assert.Equal(0.500m, Facial.UltimoLimiar);
        var perfil = await PerfilAsync(alvo); // a mesma sessão de antes da promoção
        Assert.Equal(nivel, perfil.Nivel);
    }

    [Fact]
    public async Task Rebaixar_tira_o_acesso_na_hora()
    {
        var admin = await AdminAsync();
        var alvo = await _cliente.NovoTitularComRostoAsync();
        await _cliente.DefinirNivelAsync(admin, alvo, 2);

        await _cliente.DefinirNivelAsync(admin, alvo, 1);

        Assert.Equal(1, (await PerfilAsync(alvo)).Nivel);
        var nivel2 = await _cliente.Enviar(HttpMethod.Post, "/v1/biometria/desafios", alvo.Sessao, new { finalidade = "ACESSO_NIVEL_2" });
        Assert.Equal(HttpStatusCode.Forbidden, nivel2.StatusCode);
    }

    [Fact]
    public async Task Admin_faz_outro_admin_que_passa_a_ver_o_painel_e_nao_pode_tirar_o_proprio_perfil()
    {
        var admin = await AdminAsync();
        var novo = await _cliente.NovoTitularComRostoAsync();

        var promocao = await _cliente.AlterarAcessoAsync(admin, novo.Sessao.Usuario.Id, nivel: null, ehAdmin: true);
        var lista = await _cliente.Enviar(HttpMethod.Get, "/v1/admin/usuarios", novo.Sessao);
        var autoRevogacao = await _cliente.AlterarAcessoAsync(novo, novo.Sessao.Usuario.Id, nivel: null, ehAdmin: false);
        var revogadoPorOutro = await _cliente.AlterarAcessoAsync(admin, novo.Sessao.Usuario.Id, nivel: null, ehAdmin: false);

        Assert.Equal(HttpStatusCode.NoContent, promocao.StatusCode);
        Assert.Equal(HttpStatusCode.OK, lista.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, autoRevogacao.StatusCode);
        Assert.Equal("CHANGE_NOT_ALLOWED", await Code(autoRevogacao));
        Assert.Equal(HttpStatusCode.NoContent, revogadoPorOutro.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _cliente.Enviar(HttpMethod.Get, "/v1/admin/usuarios", novo.Sessao)).StatusCode);
    }

    [Fact]
    public async Task O_admin_da_configuracao_nao_sai_pelo_painel_nem_por_outro_admin()
    {
        var admin = await AdminAsync();
        var outro = await _cliente.NovoTitularComRostoAsync();
        await _cliente.AlterarAcessoAsync(admin, outro.Sessao.Usuario.Id, nivel: null, ehAdmin: true);

        var resposta = await _cliente.AlterarAcessoAsync(outro, admin.Sessao.Usuario.Id, nivel: null, ehAdmin: false);

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal("CHANGE_NOT_ALLOWED", await Code(resposta));
        Assert.True((await PerfilAsync(admin)).Admin);
    }

    [Fact]
    public async Task So_o_rosto_confirma_e_a_senha_nao_vale()
    {
        var admin = await AdminAsync();
        var alvo = await _cliente.NovoTitularComRostoAsync();
        var desafio = await _cliente.DesafioDeAcessoAsync(admin, alvo.Sessao.Usuario.Id, 2, null);

        var comSenha = await _cliente.AlterarAcessoAsync(admin, alvo.Sessao.Usuario.Id, 2, desafio: desafio, rosto: false, senha: ClienteDeTeste.Senha);
        var comRosto = await _cliente.AlterarAcessoAsync(admin, alvo.Sessao.Usuario.Id, 2, desafio: desafio);

        Assert.Equal(HttpStatusCode.Forbidden, comSenha.StatusCode);
        Assert.Equal("FACE_REQUIRED", await Code(comSenha));
        Assert.Equal(HttpStatusCode.NoContent, comRosto.StatusCode); // a senha não gastou o desafio
    }

    [Fact]
    public async Task O_rosto_vale_so_para_aquele_usuario_e_aqueles_valores()
    {
        var admin = await AdminAsync();
        var a = await _cliente.NovoTitularComRostoAsync();
        var b = await _cliente.NovoTitularComRostoAsync();
        var paraA = await _cliente.DesafioDeAcessoAsync(admin, a.Sessao.Usuario.Id, 2, null);
        var comOutroUsuario = await _cliente.AlterarAcessoAsync(admin, b.Sessao.Usuario.Id, 2, desafio: paraA);
        var paraNivel2 = await _cliente.DesafioDeAcessoAsync(admin, a.Sessao.Usuario.Id, 2, null);
        var comOutroValor = await _cliente.AlterarAcessoAsync(admin, a.Sessao.Usuario.Id, 3, desafio: paraNivel2);

        Assert.Equal(HttpStatusCode.Unauthorized, comOutroUsuario.StatusCode);
        Assert.Equal("FACE_NOT_VERIFIED", await Code(comOutroUsuario));
        Assert.Equal(HttpStatusCode.Unauthorized, comOutroValor.StatusCode);
        Assert.Equal(1, (await PerfilAsync(a)).Nivel);
        Assert.Equal(1, (await PerfilAsync(b)).Nivel);
    }

    [Fact]
    public async Task Rosto_que_nao_bate_nao_muda_nada()
    {
        var admin = await AdminAsync();
        var alvo = await _cliente.NovoTitularComRostoAsync();
        var desafio = await _cliente.DesafioDeAcessoAsync(admin, alvo.Sessao.Usuario.Id, 2, null);

        Facial.Corresponde = false;
        var resposta = await _cliente.AlterarAcessoAsync(admin, alvo.Sessao.Usuario.Id, 2, desafio: desafio);
        Facial.Corresponde = true;

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
        Assert.Equal("FACE_NOT_VERIFIED", await Code(resposta));
        Assert.Equal(1, (await PerfilAsync(alvo)).Nivel);
    }

    [Fact]
    public async Task Quem_nao_e_admin_nem_abre_o_desafio_e_sem_desafio_pede_a_confirmacao()
    {
        var admin = await AdminAsync();
        var servidor = await _cliente.NovoTitularComRostoAsync();

        var desafio = await _cliente.Enviar(HttpMethod.Post, "/v1/biometria/desafios", servidor.Sessao,
            new { finalidade = "ALTERAR_ACESSO", parametros = new { usuario_id = servidor.Sessao.Usuario.Id, nivel = 3 } });
        var direto = await _cliente.AlterarAcessoAsync(servidor, servidor.Sessao.Usuario.Id, 3, desafio: Guid.NewGuid());
        var semDesafio = await _cliente.AlterarAcessoAsync(admin, servidor.Sessao.Usuario.Id, 3, semDesafio: true);

        Assert.Equal(HttpStatusCode.Forbidden, desafio.StatusCode);
        Assert.Equal("ADMIN_REQUIRED", await Code(desafio));
        Assert.Equal(HttpStatusCode.Forbidden, direto.StatusCode);
        Assert.Equal("ADMIN_REQUIRED", await Code(direto));
        Assert.Equal(HttpStatusCode.Forbidden, semDesafio.StatusCode);
        Assert.Equal("STEP_UP_REQUIRED", await Code(semDesafio));
        Assert.Equal(1, (await PerfilAsync(servidor)).Nivel);
    }

    [Fact]
    public async Task Pedidos_invalidos_e_usuarios_que_nao_existem_sao_recusados()
    {
        var admin = await AdminAsync();
        var alvo = await _cliente.NovoTitularComRostoAsync();
        var (_, pendente) = await _cliente.CadastrarPendenteAsync();

        var nivel4 = await _cliente.Enviar(HttpMethod.Post, "/v1/biometria/desafios", admin.Sessao, new { finalidade = "ALTERAR_ACESSO", parametros = new { usuario_id = alvo.Sessao.Usuario.Id, nivel = 4 } });
        var vazio = await _cliente.Enviar(HttpMethod.Post, "/v1/biometria/desafios", admin.Sessao, new { finalidade = "ALTERAR_ACESSO", parametros = new { usuario_id = alvo.Sessao.Usuario.Id } });
        var inexistente = await _cliente.AlterarAcessoAsync(admin, Guid.NewGuid(), 2);
        var aindaPendente = await _cliente.AlterarAcessoAsync(admin, pendente.UsuarioId, 2);

        Assert.Equal(HttpStatusCode.BadRequest, nivel4.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, vazio.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        Assert.Equal("USER_NOT_FOUND", await Code(inexistente));
        Assert.Equal(HttpStatusCode.NotFound, aindaPendente.StatusCode);
    }

    [Fact]
    public async Task Toda_mudanca_fica_no_relatorio_com_quem_mudou_quem_e_para_que()
    {
        var admin = await AdminAsync();
        var ministro = await _cliente.NovoTitularComRostoAsync();
        await _cliente.DefinirNivelAsync(admin, ministro, 3);
        var alvo = await _cliente.NovoTitularComRostoAsync();
        await _cliente.DefinirNivelAsync(admin, alvo, 2);
        Facial.Corresponde = false;
        await _cliente.AlterarAcessoAsync(admin, alvo.Sessao.Usuario.Id, 3);
        Facial.Corresponde = true;

        var resumo = await (await _cliente.Enviar(HttpMethod.Get, "/v1/relatorios/resumo", ministro.Sessao)).Ler<RelatorioResponse>();

        Assert.Contains(resumo.Recentes, a => a.Evento == "ALTERACAO_DE_ACESSO" && a.Resultado == "ACEITO"
            && a.Usuario == admin.Sessao.Usuario.Nome && a.Detalhe!.Contains(alvo.Sessao.Usuario.Nome, StringComparison.Ordinal) && a.Detalhe.Contains("nível 2", StringComparison.Ordinal));
        Assert.Contains(resumo.Recentes, a => a.Evento == "ALTERACAO_DE_ACESSO" && a.Resultado == "RECUSADO" && a.Motivo == "FACE_NOT_VERIFIED");
        Assert.Contains(resumo.PorEvento, c => c.Nome == "ALTERACAO_DE_ACESSO");
    }
}
