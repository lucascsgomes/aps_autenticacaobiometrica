using System.Net;
using System.Text.Json;
using Banco.Contratos;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Banco.Tests.Integracao;

/// <summary>Transferência por senha de ponta a ponta: intenção, confirmação idempotente, comprovante, falhas e bloqueio.</summary>
public class TransferenciaTests(ApiFactory fabrica) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _cliente = fabrica.CreateClient();

    private static string NovaChave() => Guid.NewGuid().ToString();

    private async Task<(Titular Origem, Titular Destino)> DoisTitularesAsync() =>
        (await _cliente.NovoTitularAsync(), await _cliente.NovoTitularAsync("Beatriz Souza Lima"));

    private static async Task<string?> Code(HttpResponseMessage resposta)
    {
        using var doc = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private async Task<IntencaoResponse> IntencaoAsync(Titular de, Titular para, long centavos, string? descricao = null, HttpClient? cliente = null)
    {
        var resposta = await (cliente ?? _cliente).Enviar(HttpMethod.Post, "/v1/transferencias/intencoes", de.Sessao, new { chave_destino = para.Cpf, valor_centavos = centavos, descricao });
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        return await resposta.Ler<IntencaoResponse>();
    }

    private Task<HttpResponseMessage> Confirmar(Titular quem, Guid id, string senha, string? chave) =>
        _cliente.Enviar(HttpMethod.Post, $"/v1/transferencias/{id}/confirmar", quem.Sessao, new { senha }, chave);

    private async Task<long> SaldoAsync(Titular quem) =>
        (await (await _cliente.Enviar(HttpMethod.Get, "/v1/contas/me", quem.Sessao)).Ler<ContaMeResponse>()).SaldoCentavos;

    [Fact]
    public async Task Transferencia_por_senha_move_o_dinheiro_e_gera_comprovante_para_os_dois_lados()
    {
        var (ana, bia) = await DoisTitularesAsync();

        var intencao = await IntencaoAsync(ana, bia, 15_000, "Aluguel de outubro");
        Assert.Equal("PENDENTE_BIOMETRIA", intencao.Status);
        Assert.Equal(["SENHA"], intencao.Metodos);
        Assert.Equal("Beatriz L***", intencao.Resumo.Destino.NomeMascarado);
        Assert.Equal(485_000, intencao.Resumo.SaldoAposCentavos);
        Assert.Contains("R$ 150,00", intencao.Instrucao, StringComparison.Ordinal);
        Assert.Equal(500_000, await SaldoAsync(ana)); // intenção não move dinheiro

        var resposta = await Confirmar(ana, intencao.TransferenciaId, ClienteDeTeste.Senha, NovaChave());
        var comprovante = await resposta.Ler<ComprovanteResponse>();

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("CONCLUIDA", comprovante.Status);
        Assert.Equal("SENHA", comprovante.MetodoConfirmacao);
        Assert.Matches("^E[0-9]{8}[0-9]{12}[A-Za-z0-9]{11}$", comprovante.E2eId);
        Assert.Matches("^[0-9A-F]{4}(-[0-9A-F]{4}){3}$", comprovante.Autenticacao);
        Assert.Equal(485_000, comprovante.SaldoAposCentavos);
        Assert.Equal("Ana Beatriz Moreira", comprovante.Origem.Nome);
        Assert.Equal("Beatriz L***", comprovante.Destino.Nome);
        Assert.Equal(485_000, await SaldoAsync(ana));
        Assert.Equal(515_000, await SaldoAsync(bia));

        // Quem recebe vê a própria posição e a origem mascarada.
        var doDestino = await (await _cliente.Enviar(HttpMethod.Get, $"/v1/transferencias/{intencao.TransferenciaId}/comprovante", bia.Sessao)).Ler<ComprovanteResponse>();
        Assert.Equal(515_000, doDestino.SaldoAposCentavos);
        Assert.Equal("Ana M***", doDestino.Origem.Nome);

        var situacao = await (await _cliente.Enviar(HttpMethod.Get, $"/v1/transferencias/{intencao.TransferenciaId}", ana.Sessao)).Ler<TransferenciaResponse>();
        Assert.Equal(["PENDENTE_BIOMETRIA", "PROCESSANDO", "CONCLUIDA"], situacao.Historico.Select(h => h.Para));
    }

    [Fact]
    public async Task Repetir_o_confirmar_com_a_mesma_chave_devolve_a_mesma_resposta_sem_mover_dinheiro_de_novo()
    {
        var (ana, bia) = await DoisTitularesAsync();
        var intencao = await IntencaoAsync(ana, bia, 10_000);
        var chave = NovaChave();

        var primeira = await Confirmar(ana, intencao.TransferenciaId, ClienteDeTeste.Senha, chave);
        var original = await primeira.Ler<ComprovanteResponse>();
        var repetida = await Confirmar(ana, intencao.TransferenciaId, ClienteDeTeste.Senha, chave);

        Assert.Equal(HttpStatusCode.OK, repetida.StatusCode);
        Assert.Equal(original, await repetida.Ler<ComprovanteResponse>()); // o jsonb reordena as chaves, o conteúdo é o mesmo
        Assert.Equal(490_000, await SaldoAsync(ana));
        Assert.Equal(510_000, await SaldoAsync(bia));
    }

    [Fact]
    public async Task Chave_de_idempotencia_e_obrigatoria_e_nao_serve_para_outra_transferencia()
    {
        var (ana, bia) = await DoisTitularesAsync();
        var primeira = await IntencaoAsync(ana, bia, 1_000);

        var semChave = await Confirmar(ana, primeira.TransferenciaId, ClienteDeTeste.Senha, null);
        var chave = NovaChave();
        await Confirmar(ana, primeira.TransferenciaId, ClienteDeTeste.Senha, chave);
        var segunda = await IntencaoAsync(ana, bia, 2_000);
        var reusada = await Confirmar(ana, segunda.TransferenciaId, ClienteDeTeste.Senha, chave);
        var jaConfirmada = await Confirmar(ana, primeira.TransferenciaId, ClienteDeTeste.Senha, NovaChave());

        Assert.Equal(HttpStatusCode.BadRequest, semChave.StatusCode);
        Assert.Equal("IDEMPOTENCY_KEY_REQUIRED", await Code(semChave));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, reusada.StatusCode);
        Assert.Equal("IDEMPOTENCY_KEY_REUSED", await Code(reusada));
        Assert.Equal(HttpStatusCode.Conflict, jaConfirmada.StatusCode);
        Assert.Equal("TRANSFER_NOT_PENDING", await Code(jaConfirmada));
    }

    [Fact]
    public async Task Senha_errada_cancela_a_transferencia_sem_mover_dinheiro_e_a_resposta_se_repete()
    {
        var (ana, bia) = await DoisTitularesAsync();
        var intencao = await IntencaoAsync(ana, bia, 10_000);
        var chave = NovaChave();

        var errada = await Confirmar(ana, intencao.TransferenciaId, "senha-errada-123", chave);
        var repetida = await Confirmar(ana, intencao.TransferenciaId, "senha-errada-123", chave);
        var situacao = await (await _cliente.Enviar(HttpMethod.Get, $"/v1/transferencias/{intencao.TransferenciaId}", ana.Sessao)).Ler<TransferenciaResponse>();

        Assert.Equal(HttpStatusCode.Unauthorized, errada.StatusCode);
        Assert.Equal("PASSWORD_NOT_VERIFIED", await Code(errada));
        Assert.Equal(HttpStatusCode.Unauthorized, repetida.StatusCode);
        Assert.Equal("FALHA", situacao.Status);
        Assert.Equal("SENHA_RECUSADA", situacao.MotivoFalha);
        Assert.Equal(500_000, await SaldoAsync(ana));
        Assert.Equal(500_000, await SaldoAsync(bia));
        Assert.Equal(HttpStatusCode.NotFound, (await _cliente.Enviar(HttpMethod.Get, $"/v1/transferencias/{intencao.TransferenciaId}/comprovante", ana.Sessao)).StatusCode);
    }

    [Fact]
    public async Task Cinco_senhas_erradas_bloqueiam_e_a_senha_certa_depois_recebe_403_sem_mover_dinheiro()
    {
        var (ana, bia) = await DoisTitularesAsync();
        for (var i = 0; i < 5; i++)
        {
            var intencao = await IntencaoAsync(ana, bia, 1_000);
            await Confirmar(ana, intencao.TransferenciaId, "senha-errada-123", NovaChave());
        }

        var nova = await IntencaoAsync(ana, bia, 1_000);
        var certa = await Confirmar(ana, nova.TransferenciaId, ClienteDeTeste.Senha, NovaChave());

        Assert.Equal(HttpStatusCode.Forbidden, certa.StatusCode);
        Assert.Equal("ACCOUNT_LOCKED", await Code(certa));
        Assert.True(certa.Headers.RetryAfter?.Delta > TimeSpan.Zero);
        Assert.Equal(500_000, await SaldoAsync(ana));
    }

    [Fact]
    public async Task Intencao_recusa_destino_invalido_saldo_curto_e_valor_zero()
    {
        var (ana, bia) = await DoisTitularesAsync();
        Task<HttpResponseMessage> Intencao(object corpo) => _cliente.Enviar(HttpMethod.Post, "/v1/transferencias/intencoes", ana.Sessao, corpo);

        var paraSi = await Intencao(new { chave_destino = ana.Cpf, valor_centavos = 100 });
        var semSaldo = await Intencao(new { chave_destino = bia.Cpf, valor_centavos = 500_001 });
        var desconhecida = await Intencao(new { chave_destino = "52998224725", valor_centavos = 100 });
        var malFormada = await Intencao(new { chave_destino = "529.982.247-25", valor_centavos = 100 });
        var zero = await Intencao(new { chave_destino = bia.Cpf, valor_centavos = 0 });

        Assert.Equal("SAME_ACCOUNT", await Code(paraSi));
        Assert.Equal("INSUFFICIENT_FUNDS", await Code(semSaldo));
        Assert.Equal(HttpStatusCode.NotFound, desconhecida.StatusCode);
        Assert.Equal("INVALID_KEY_FORMAT", await Code(malFormada));
        Assert.Equal("VALIDATION_ERROR", await Code(zero));
    }

    [Fact]
    public async Task Nova_intencao_expira_a_anterior_e_so_o_dono_confirma()
    {
        var (ana, bia) = await DoisTitularesAsync();
        var antiga = await IntencaoAsync(ana, bia, 1_000);
        var atual = await IntencaoAsync(ana, bia, 2_000);

        var expirada = await Confirmar(ana, antiga.TransferenciaId, ClienteDeTeste.Senha, NovaChave());
        var alheia = await Confirmar(bia, atual.TransferenciaId, ClienteDeTeste.Senha, NovaChave());

        Assert.Equal("TRANSFER_NOT_PENDING", await Code(expirada));
        Assert.Equal(HttpStatusCode.NotFound, alheia.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _cliente.Enviar(HttpMethod.Get, $"/v1/transferencias/{atual.TransferenciaId}", bia.Sessao)).StatusCode);
    }

    [Fact]
    public async Task Confirmar_depois_de_90_segundos_expira_a_intencao_e_nao_gasta_a_chave()
    {
        var relogio = new RelogioAjustavel();
        using var comRelogio = fabrica.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<TimeProvider>(relogio)));
        var cliente = comRelogio.CreateClient();
        var ana = await cliente.NovoTitularAsync();
        var bia = await cliente.NovoTitularAsync("Beatriz Souza Lima");
        var intencao = await IntencaoAsync(ana, bia, 1_000, cliente: cliente);
        var chave = NovaChave();

        relogio.Avancar(TimeSpan.FromSeconds(91));
        var tarde = await cliente.Enviar(HttpMethod.Post, $"/v1/transferencias/{intencao.TransferenciaId}/confirmar", ana.Sessao, new { senha = ClienteDeTeste.Senha }, chave);

        Assert.Equal(HttpStatusCode.Conflict, tarde.StatusCode);
        Assert.Equal("TRANSFER_EXPIRED", await Code(tarde));
        var situacao = await (await cliente.Enviar(HttpMethod.Get, $"/v1/transferencias/{intencao.TransferenciaId}", ana.Sessao)).Ler<TransferenciaResponse>();
        Assert.Equal("EXPIRADA", situacao.Status);
    }

    [Fact]
    public async Task Confirmacoes_simultaneas_com_chaves_diferentes_movem_o_dinheiro_uma_vez_so()
    {
        var (ana, bia) = await DoisTitularesAsync();
        var intencao = await IntencaoAsync(ana, bia, 10_000);

        var respostas = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Confirmar(ana, intencao.TransferenciaId, ClienteDeTeste.Senha, NovaChave())));

        Assert.Equal(1, respostas.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.All(respostas.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Equal(490_000, await SaldoAsync(ana));
        Assert.Equal(510_000, await SaldoAsync(bia));
    }

    [Fact]
    public async Task Rotas_de_transferencia_exigem_login()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _cliente.Enviar(HttpMethod.Post, "/v1/transferencias/intencoes", null, new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _cliente.Enviar(HttpMethod.Get, $"/v1/transferencias/{Guid.NewGuid()}", null)).StatusCode);
    }

    private sealed class RelogioAjustavel : TimeProvider
    {
        private TimeSpan _deslocamento;

        public void Avancar(TimeSpan quanto) => _deslocamento += quanto;

        public override DateTimeOffset GetUtcNow() => System.GetUtcNow() + _deslocamento;
    }
}
