using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ministerio.Contratos;
using Ministerio.Dominio;
using Ministerio.Infra.Persistencia;

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

    // ---- processamento do rosto, exclusão de cadastro e exclusão da própria conta ----

    /// <summary>Abre o desafio (se não vier um) e manda a ação em multipart: rosto, ou senha. Devolve a resposta do primeiro passo que falhar.</summary>
    private async Task<HttpResponseMessage> AcaoAsync(Titular quem, HttpMethod metodo, string rota, string finalidade, object parametros, object? dados = null, bool rosto = true, string? senha = null, Guid? desafio = null)
    {
        if (desafio is null)
        {
            var aberto = await _cliente.Enviar(HttpMethod.Post, "/v1/biometria/desafios", quem.Sessao, new { finalidade, parametros });
            if (aberto.StatusCode != HttpStatusCode.Created)
            {
                return aberto;
            }

            desafio = (await aberto.Ler<DesafioDeAcaoResponse>()).DesafioId;
        }

        var requisicao = new HttpRequestMessage(metodo, rota);
        requisicao.Headers.Authorization = new AuthenticationHeaderValue("Bearer", quem.Sessao.AccessToken);
        var corpo = new MultipartFormDataContent { { new StringContent(desafio.Value.ToString()), "desafio_id" } };
        if (dados is not null)
        {
            corpo.Add(new StringContent(JsonSerializer.Serialize(dados, ClienteDeTeste.Json)), "dados");
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
        return await _cliente.SendAsync(requisicao);
    }

    private Task<HttpResponseMessage> ProcessarAsync(Titular admin, string pre, string ruido, bool rosto = true, string? senha = null)
    {
        var pedido = new { pre_processamento = pre, tratamento_ruido = ruido };
        return AcaoAsync(admin, HttpMethod.Put, "/v1/admin/processamento", "ALTERAR_PROCESSAMENTO", pedido, pedido, rosto, senha);
    }

    private Task<HttpResponseMessage> ExcluirAsync(Titular quem, Guid alvo, bool rosto = true, string? senha = null, Guid? desafio = null) =>
        AcaoAsync(quem, HttpMethod.Delete, $"/v1/admin/usuarios/{alvo}", "EXCLUIR_CADASTRO", new { usuario_id = alvo }, null, rosto, senha, desafio);

    private async Task<bool> TemRostoNoBancoAsync(Guid usuarioId)
    {
        using var escopo = _fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<MinisterioDbContext>();
        return await db.Templates.AnyAsync(x => x.UsuarioId == usuarioId);
    }

    private async Task<bool> ExisteNoBancoAsync(Guid usuarioId)
    {
        using var escopo = _fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<MinisterioDbContext>();
        return await db.Usuarios.AnyAsync(x => x.Id == usuarioId);
    }

    [Fact]
    public async Task O_admin_escolhe_o_processamento_com_o_rosto_e_a_escolha_fica_no_relatorio()
    {
        var admin = await AdminAsync();
        var ministro = await _cliente.NovoTitularComRostoAsync();
        await _cliente.DefinirNivelAsync(admin, ministro, 3);

        var mudou = await ProcessarAsync(admin, "gaussiano", "mediana");
        var atual = await (await _cliente.Enviar(HttpMethod.Get, "/v1/admin/processamento", admin.Sessao)).Ler<ProcessamentoDto>();
        var resumo = await (await _cliente.Enviar(HttpMethod.Get, "/v1/relatorios/resumo", ministro.Sessao)).Ler<RelatorioResponse>();
        await ProcessarAsync(admin, "clahe", "nenhum");

        Assert.Equal(HttpStatusCode.NoContent, mudou.StatusCode);
        Assert.Equal("gaussiano", atual.PreProcessamento);
        Assert.Equal("mediana", atual.TratamentoRuido);
        Assert.Contains("clahe", atual.PreProcessamentos);
        Assert.Contains("nenhum", atual.TratamentosDeRuido);
        Assert.Contains(resumo.Recentes, a => a.Evento == "ALTERACAO_DE_PROCESSAMENTO" && a.Resultado == "ACEITO" && a.Detalhe!.Contains("gaussiano", StringComparison.Ordinal));
    }

    [Fact]
    public async Task So_admin_ve_e_muda_o_processamento()
    {
        var servidor = await _cliente.NovoTitularComRostoAsync();

        var ler = await _cliente.Enviar(HttpMethod.Get, "/v1/admin/processamento", servidor.Sessao);
        var mudar = await ProcessarAsync(servidor, "gaussiano", "nenhum");

        Assert.Equal(HttpStatusCode.Forbidden, ler.StatusCode);
        Assert.Equal("ADMIN_REQUIRED", await Code(ler));
        Assert.Equal(HttpStatusCode.Forbidden, mudar.StatusCode);
        Assert.Equal("ADMIN_REQUIRED", await Code(mudar));
    }

    [Fact]
    public async Task Processamento_recusa_opcao_inexistente_e_a_senha_nao_confirma()
    {
        var admin = await AdminAsync();

        var invalido = await ProcessarAsync(admin, "inexistente", "nenhum");
        var comSenha = await ProcessarAsync(admin, "equalizacao", "nenhum", rosto: false, senha: ClienteDeTeste.Senha);

        Assert.Equal(HttpStatusCode.BadRequest, invalido.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, comSenha.StatusCode);
        Assert.Equal("FACE_REQUIRED", await Code(comSenha));
    }

    [Fact]
    public async Task Rosto_que_nao_bate_nao_muda_o_processamento()
    {
        var admin = await AdminAsync();

        Facial.Corresponde = false;
        var resposta = await ProcessarAsync(admin, "equalizacao", "fechamento");
        Facial.Corresponde = true;
        var atual = await (await _cliente.Enviar(HttpMethod.Get, "/v1/admin/processamento", admin.Sessao)).Ler<ProcessamentoDto>();

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
        Assert.Equal("FACE_NOT_VERIFIED", await Code(resposta));
        Assert.NotEqual("equalizacao", atual.PreProcessamento);
    }

    [Fact]
    public async Task Admin_exclui_o_cadastro_e_o_vetor_do_rosto_some()
    {
        var admin = await AdminAsync();
        var ministro = await _cliente.NovoTitularComRostoAsync();
        await _cliente.DefinirNivelAsync(admin, ministro, 3);
        var alvo = await _cliente.NovoTitularComRostoAsync();
        var id = alvo.Sessao.Usuario.Id;
        Assert.True(await TemRostoNoBancoAsync(id));

        var resposta = await ExcluirAsync(admin, id);

        Assert.Equal(HttpStatusCode.NoContent, resposta.StatusCode);
        Assert.False(await ExisteNoBancoAsync(id));
        Assert.False(await TemRostoNoBancoAsync(id));
        var lista = await (await _cliente.Enviar(HttpMethod.Get, "/v1/admin/usuarios", admin.Sessao)).Ler<List<UsuarioAdminDto>>();
        Assert.DoesNotContain(lista, u => u.Id == id);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _cliente.Enviar(HttpMethod.Get, "/v1/me", alvo.Sessao)).StatusCode);
        var resumo = await (await _cliente.Enviar(HttpMethod.Get, "/v1/relatorios/resumo", ministro.Sessao)).Ler<RelatorioResponse>();
        Assert.Contains(resumo.Recentes, a => a.Evento == "EXCLUSAO_DE_CADASTRO" && a.Resultado == "ACEITO" && a.Detalhe!.Contains(alvo.Sessao.Usuario.Nome, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Excluir_cadastro_e_so_do_admin_e_so_com_o_rosto()
    {
        var admin = await AdminAsync();
        var servidor = await _cliente.NovoTitularComRostoAsync();
        var alvo = await _cliente.NovoTitularComRostoAsync();

        var porServidor = await ExcluirAsync(servidor, alvo.Sessao.Usuario.Id);
        var comSenha = await ExcluirAsync(admin, alvo.Sessao.Usuario.Id, rosto: false, senha: ClienteDeTeste.Senha);
        Facial.Corresponde = false;
        var rostoErrado = await ExcluirAsync(admin, alvo.Sessao.Usuario.Id);
        Facial.Corresponde = true;

        Assert.Equal(HttpStatusCode.Forbidden, porServidor.StatusCode);
        Assert.Equal("ADMIN_REQUIRED", await Code(porServidor));
        Assert.Equal(HttpStatusCode.Forbidden, comSenha.StatusCode);
        Assert.Equal("FACE_REQUIRED", await Code(comSenha));
        Assert.Equal(HttpStatusCode.Unauthorized, rostoErrado.StatusCode);
        Assert.True(await ExisteNoBancoAsync(alvo.Sessao.Usuario.Id));
        Assert.True(await TemRostoNoBancoAsync(alvo.Sessao.Usuario.Id));
    }

    [Fact]
    public async Task Admin_nao_exclui_a_si_mesmo_nem_o_admin_da_configuracao_nem_quem_nao_existe()
    {
        var admin = await AdminAsync();
        var outroAdmin = await _cliente.NovoTitularComRostoAsync();
        await _cliente.AlterarAcessoAsync(admin, outroAdmin.Sessao.Usuario.Id, nivel: null, ehAdmin: true);

        var aSiMesmo = await ExcluirAsync(outroAdmin, outroAdmin.Sessao.Usuario.Id);
        var fixo = await ExcluirAsync(outroAdmin, admin.Sessao.Usuario.Id);
        var inexistente = await ExcluirAsync(admin, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Conflict, aSiMesmo.StatusCode);
        Assert.Equal("CHANGE_NOT_ALLOWED", await Code(aSiMesmo));
        Assert.Equal(HttpStatusCode.Conflict, fixo.StatusCode);
        Assert.Equal("CHANGE_NOT_ALLOWED", await Code(fixo));
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        Assert.True(await ExisteNoBancoAsync(admin.Sessao.Usuario.Id));
    }

    [Fact]
    public async Task O_desafio_de_exclusao_vale_so_para_aquele_usuario()
    {
        var admin = await AdminAsync();
        var a = await _cliente.NovoTitularComRostoAsync();
        var b = await _cliente.NovoTitularComRostoAsync();
        var paraA = (await (await _cliente.Enviar(HttpMethod.Post, "/v1/biometria/desafios", admin.Sessao, new { finalidade = "EXCLUIR_CADASTRO", parametros = new { usuario_id = a.Sessao.Usuario.Id } })).Ler<DesafioDeAcaoResponse>()).DesafioId;

        var emB = await ExcluirAsync(admin, b.Sessao.Usuario.Id, desafio: paraA);

        Assert.Equal(HttpStatusCode.Unauthorized, emB.StatusCode);
        Assert.True(await ExisteNoBancoAsync(b.Sessao.Usuario.Id));
    }

    [Fact]
    public async Task O_usuario_exclui_a_propria_conta_com_a_senha_e_o_vetor_do_rosto_some()
    {
        var admin = await AdminAsync();
        var ministro = await _cliente.NovoTitularComRostoAsync();
        await _cliente.DefinirNivelAsync(admin, ministro, 3);
        var dono = await _cliente.NovoTitularComRostoAsync();
        var id = dono.Sessao.Usuario.Id;

        var errada = await AcaoAsync(dono, HttpMethod.Delete, "/v1/me", "EXCLUIR_CADASTRO", new { usuario_id = id }, rosto: false, senha: "senha-errada-qualquer");
        var certa = await AcaoAsync(dono, HttpMethod.Delete, "/v1/me", "EXCLUIR_CADASTRO", new { usuario_id = id }, rosto: false, senha: ClienteDeTeste.Senha);

        Assert.Equal(HttpStatusCode.Unauthorized, errada.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, certa.StatusCode);
        Assert.False(await ExisteNoBancoAsync(id));
        Assert.False(await TemRostoNoBancoAsync(id));
        var entrar = await _cliente.Enviar(HttpMethod.Post, "/v1/auth/login/senha", null, new { cpf = dono.Cpf, senha = ClienteDeTeste.Senha });
        Assert.Equal(HttpStatusCode.Unauthorized, entrar.StatusCode);
        var resumo = await (await _cliente.Enviar(HttpMethod.Get, "/v1/relatorios/resumo", ministro.Sessao)).Ler<RelatorioResponse>();
        Assert.Contains(resumo.Recentes, a => a.Evento == "EXCLUSAO_DE_CADASTRO" && a.Detalhe!.Contains("excluiu a própria conta", StringComparison.Ordinal));
    }

    [Fact]
    public async Task O_usuario_tambem_exclui_a_propria_conta_com_o_rosto_e_nao_apaga_a_de_outro()
    {
        var dono = await _cliente.NovoTitularComRostoAsync();
        var vitima = await _cliente.NovoTitularComRostoAsync();

        var contraOutro = await AcaoAsync(dono, HttpMethod.Delete, "/v1/me", "EXCLUIR_CADASTRO", new { usuario_id = vitima.Sessao.Usuario.Id });
        var propria = await AcaoAsync(dono, HttpMethod.Delete, "/v1/me", "EXCLUIR_CADASTRO", new { usuario_id = dono.Sessao.Usuario.Id });

        Assert.Equal(HttpStatusCode.Forbidden, contraOutro.StatusCode);
        Assert.Equal("ADMIN_REQUIRED", await Code(contraOutro));
        Assert.Equal(HttpStatusCode.NoContent, propria.StatusCode);
        Assert.True(await ExisteNoBancoAsync(vitima.Sessao.Usuario.Id));
        Assert.False(await ExisteNoBancoAsync(dono.Sessao.Usuario.Id));
    }
}
