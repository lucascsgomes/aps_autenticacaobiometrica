using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Banco.Api.Middleware;
using Banco.Api.Seguranca;
using Banco.Contratos;
using Banco.Dominio;
using Banco.Dominio.Transferencias;
using Banco.Infra.Idempotencia;
using Banco.Infra.Transferencias;
using Microsoft.AspNetCore.Http.Json;
using RequestSizeLimitAttribute = Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Banco.Api.Endpoints;

internal static class TransferenciaEndpoints
{
    private const string CabecalhoIdempotencia = "Idempotency-Key";
    private const string EscopoConfirmar = "POST /v1/transferencias/{id}/confirmar";
    private const string EscopoConfirmarComRosto = "POST /v1/transferencias/{id}/confirmar-face";

    public static IEndpointRouteBuilder MapearTransferencias(this IEndpointRouteBuilder rotas)
    {
        var grupo = rotas.MapGroup("/v1/transferencias").RequireAuthorization();
        grupo.MapPost("/intencoes", CriarIntencao).RequireRateLimiting(Limites.Transferencia);
        grupo.MapPost("/{id:guid}/confirmar", Confirmar).RequireRateLimiting(Limites.Transferencia);
        grupo.MapPost("/{id:guid}/desafio", AbrirDesafio).RequireRateLimiting(Limites.Transferencia);
        grupo.MapPost("/{id:guid}/confirmar-face", ConfirmarComRosto)
            .RequireRateLimiting(Limites.Transferencia)
            .WithMetadata(new RequestSizeLimitAttribute(Imagens.TamanhoMaximoDaRequisicao));
        grupo.MapGet("/{id:guid}", Situacao).RequireRateLimiting(Limites.Leitura);
        grupo.MapGet("/{id:guid}/comprovante", Comprovante).RequireRateLimiting(Limites.Leitura);
        return rotas;
    }

    private static Guid? UsuarioDoToken(ClaimsPrincipal usuario) =>
        Guid.TryParse(usuario.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id) ? id : null;

    private static async Task<IResult> CriarIntencao(IntencaoRequest? corpo, ClaimsPrincipal usuario, ServicoDeTransferencia servico, CancellationToken cancelamento)
    {
        if (UsuarioDoToken(usuario) is not { } usuarioId)
        {
            return Results.Unauthorized();
        }

        var erros = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(corpo?.ChaveDestino) || corpo.ChaveDestino.Length > 254)
        {
            erros["chave_destino"] = ["Informe a chave do destinatário."];
        }

        if (corpo?.ValorCentavos is null or < 1)
        {
            erros["valor_centavos"] = ["O valor precisa ser de pelo menos 1 centavo."];
        }

        if (corpo?.Descricao is { Length: > Transferencia.TamanhoMaximoDescricao })
        {
            erros["descricao"] = [$"Use no máximo {Transferencia.TamanhoMaximoDescricao} caracteres."];
        }

        if (erros.Count > 0)
        {
            return Validacao.Problema(erros);
        }

        var i = await servico.CriarIntencaoAsync(usuarioId, corpo!.ChaveDestino!, corpo.ValorCentavos!.Value, corpo.Descricao, cancelamento);
        var resposta = new IntencaoResponse(
            i.TransferenciaId,
            Codigo.De(StatusTransferencia.PendenteBiometria),
            i.ExpiraEm,
            new ResumoDto(
                new OrigemResumoDto(i.OrigemNome, i.OrigemContaMascarada),
                new DestinoResumoDto(i.DestinoNomeMascarado, i.ChaveMascarada, ServicoDeTransferencia.NomeDaInstituicao),
                i.ValorCentavos,
                0,
                i.SaldoAposCentavos),
            $"Você está confirmando {Reais(i.ValorCentavos)} para {i.DestinoNomeMascarado}. " + (i.PodeUsarRosto ? "Olhe para a câmera ou digite sua senha." : "Digite sua senha."),
            i.PodeUsarRosto ? [Codigo.De(MetodoConfirmacao.Face), Codigo.De(MetodoConfirmacao.Senha)] : [Codigo.De(MetodoConfirmacao.Senha)]);
        return Results.Json(resposta, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> AbrirDesafio(Guid id, HttpContext http, ClaimsPrincipal usuario, ServicoDeTransferencia servico, CancellationToken cancelamento)
    {
        if (UsuarioDoToken(usuario) is not { } usuarioId)
        {
            return Results.Unauthorized();
        }

        var desafio = await servico.AbrirDesafioDeRostoAsync(usuarioId, id, http.Connection.RemoteIpAddress, cancelamento);
        return Results.Ok(new DesafioResponse(desafio.Id, desafio.ExpiraEm, "Olhe para a câmera e toque em Confirmar."));
    }

    private static async Task<IResult> Confirmar(
        Guid id,
        ConfirmarRequest? corpo,
        HttpContext http,
        ClaimsPrincipal usuario,
        ServicoDeTransferencia servico,
        ServicoDeIdempotencia idempotencia,
        JwtOpcoes jwt,
        IOptions<JsonOptions> json,
        CancellationToken cancelamento)
    {
        if (UsuarioDoToken(usuario) is not { } usuarioId)
        {
            return Results.Unauthorized();
        }

        var chave = ChaveDeIdempotencia(http);
        if (string.IsNullOrEmpty(corpo?.Senha) || corpo.Senha.Length > Validacao.SenhaMaxima)
        {
            return Validacao.Problema(new Dictionary<string, string[]> { ["senha"] = ["Informe a senha."] });
        }

        return await ConfirmarIdempotenteAsync(
            http, id, usuarioId, chave, EscopoConfirmar, servico, idempotencia, jwt, json,
            ct => servico.ConfirmarComSenhaAsync(usuarioId, id, corpo.Senha, ct),
            cancelamento);
    }

    // Foto sem rosto ou com vários (422) e serviço facial fora do ar (503) deixam a transferência pendente: dá para tentar de novo.
    private static async Task<IResult> ConfirmarComRosto(
        Guid id,
        HttpContext http,
        ClaimsPrincipal usuario,
        ServicoDeTransferencia servico,
        ServicoDeIdempotencia idempotencia,
        JwtOpcoes jwt,
        IOptions<JsonOptions> json,
        CancellationToken cancelamento)
    {
        if (UsuarioDoToken(usuario) is not { } usuarioId)
        {
            return Results.Unauthorized();
        }

        var chave = ChaveDeIdempotencia(http);
        var formulario = await Imagens.LerFormularioAsync(http.Request, cancelamento);
        if (!Guid.TryParse(formulario["desafio_id"], out var desafioId))
        {
            return Validacao.Problema(new Dictionary<string, string[]> { ["desafio_id"] = ["Informe o desafio recebido em transferencias/{id}/desafio."] });
        }

        var imagem = Imagens.LerImagem(formulario);
        return await ConfirmarIdempotenteAsync(
            http, id, usuarioId, chave, EscopoConfirmarComRosto, servico, idempotencia, jwt, json,
            ct => servico.ConfirmarComRostoAsync(usuarioId, id, desafioId, imagem, http.TraceIdentifier, ct),
            cancelamento);
    }

    private static string ChaveDeIdempotencia(HttpContext http)
    {
        var chave = http.Request.Headers[CabecalhoIdempotencia].ToString();
        return chave.Length is < 8 or > 255 ? throw new ChaveDeIdempotenciaObrigatoriaException() : chave;
    }

    private static async Task<IResult> ConfirmarIdempotenteAsync(
        HttpContext http,
        Guid id,
        Guid usuarioId,
        string chave,
        string escopo,
        ServicoDeTransferencia servico,
        ServicoDeIdempotencia idempotencia,
        JwtOpcoes jwt,
        IOptions<JsonOptions> json,
        Func<CancellationToken, Task<Transferencia>> confirmar,
        CancellationToken cancelamento)
    {
        var opcoes = json.Value.SerializerOptions;
        var reserva = await idempotencia.ReservarAsync(usuarioId, chave, escopo, ServicoDeIdempotencia.Hash(escopo, id), cancelamento);
        switch (reserva.Situacao)
        {
            case SituacaoDaReserva.ChaveReusada:
                throw new ChaveDeIdempotenciaReusadaException();
            case SituacaoDaReserva.EmAndamento:
                throw new RequisicaoEmAndamentoException();
            case SituacaoDaReserva.Concluida:
                return Resposta(reserva.StatusCode!.Value, reserva.Corpo!);
        }

        int status;
        string conteudo;
        try
        {
            var t = await confirmar(cancelamento);
            if (t.Status == StatusTransferencia.Concluida)
            {
                var comprovante = await servico.ObterComprovanteAsync(usuarioId, id, CancellationToken.None);
                (status, conteudo) = (StatusCodes.Status200OK, JsonSerializer.Serialize(Montar(comprovante!, jwt), opcoes));
            }
            else
            {
                var problema = TratadorGlobalDeExcecoes.MontarProblema(ErroDaFalha(t.MotivoFalha));
                problema.Extensions["traceId"] = http.TraceIdentifier;
                (status, conteudo) = (problema.Status!.Value, JsonSerializer.Serialize(problema, opcoes));
            }
        }
        catch (NegocioException)
        {
            // Nada mudou na transferência: devolve a chave para o cliente poder tentar de novo com ela.
            await idempotencia.LiberarAsync(usuarioId, chave, CancellationToken.None);
            throw;
        }

        // Qualquer outra exceção deixa a chave em andamento: o dinheiro pode ter andado, e repetir às cegas seria pior.
        await idempotencia.ConcluirAsync(usuarioId, chave, status, conteudo, CancellationToken.None);
        return Resposta(status, conteudo);
    }

    private static async Task<IResult> Situacao(Guid id, ClaimsPrincipal usuario, ServicoDeTransferencia servico, CancellationToken cancelamento)
    {
        if (UsuarioDoToken(usuario) is not { } usuarioId)
        {
            return Results.Unauthorized();
        }

        var s = await servico.ObterSituacaoAsync(usuarioId, id, cancelamento) ?? throw new TransferenciaNaoEncontradaException();
        return Results.Ok(new TransferenciaResponse(
            s.TransferenciaId,
            Codigo.De(s.Status),
            s.ValorCentavos,
            s.MotivoFalha,
            s.CriadaEm,
            s.ConcluidaEm,
            s.Historico.Select(h => new HistoricoDto(h.De, h.Para, h.Em)).ToList()));
    }

    private static async Task<IResult> Comprovante(Guid id, ClaimsPrincipal usuario, ServicoDeTransferencia servico, JwtOpcoes jwt, CancellationToken cancelamento)
    {
        if (UsuarioDoToken(usuario) is not { } usuarioId)
        {
            return Results.Unauthorized();
        }

        var c = await servico.ObterComprovanteAsync(usuarioId, id, cancelamento) ?? throw new TransferenciaNaoEncontradaException();
        return Results.Ok(Montar(c, jwt));
    }

    private static NegocioException ErroDaFalha(string? motivo) => motivo switch
    {
        MotivoFalha.SenhaRecusada => new SenhaNaoVerificadaException(),
        MotivoFalha.BiometriaRecusada => new FaceNaoVerificadaException(),
        MotivoFalha.SaldoInsuficiente => new SaldoInsuficienteException(),
        MotivoFalha.DestinoIndisponivel => new DestinoIndisponivelException(),
        MotivoFalha.LimiteNoturno => new LimiteNoturnoExcedidoException(),
        MotivoFalha.LimiteDiurno => new LimiteDiurnoExcedidoException(),
        _ => throw new InvalidOperationException($"Motivo de falha inesperado na confirmação: {motivo}"),
    };

    private static ComprovanteResponse Montar(ComprovanteDados c, JwtOpcoes jwt) => new(
        c.TransferenciaId,
        c.E2eId,
        Codigo.De(StatusTransferencia.Concluida),
        Codigo.De(c.Tipo),
        c.ValorCentavos,
        0,
        c.Descricao,
        new ParteDto(c.Origem.Nome, c.Origem.Agencia, c.Origem.ContaMascarada),
        new ParteDto(c.Destino.Nome, c.Destino.Agencia, c.Destino.ContaMascarada),
        c.ConcluidaEm,
        c.SaldoAposCentavos,
        Codigo.De(c.Metodo),
        CodigoDeAutenticacao(jwt, c));

    /// <summary>Código simulado (não é assinatura jurídica): 16 hex do HMAC do E2E e do valor, em grupos de 4.</summary>
    private static string CodigoDeAutenticacao(JwtOpcoes jwt, ComprovanteDados c)
    {
        var hmac = HMACSHA256.HashData(jwt.Chave.Key, Encoding.UTF8.GetBytes($"comprovante|{c.E2eId}|{c.ValorCentavos}"));
        var hex = Convert.ToHexString(hmac.AsSpan(0, 8));
        return string.Join('-', Enumerable.Range(0, 4).Select(i => hex.Substring(i * 4, 4)));
    }

    private static IResult Resposta(int status, string corpo) =>
        Results.Text(corpo, status == StatusCodes.Status200OK ? "application/json" : "application/problem+json", Encoding.UTF8, status);

    private static string Reais(long centavos) =>
        $"R$ {(centavos / 100).ToString("N0", CultureInfo.InvariantCulture).Replace(',', '.')},{centavos % 100:00}";
}
