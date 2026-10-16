using System.Security.Claims;
using System.Text.Json;
using Ministerio.Api.Seguranca;
using Ministerio.Contratos;
using Ministerio.Dominio;
using Ministerio.Dominio.Acesso;
using Ministerio.Dominio.Biometria;
using Ministerio.Infra.Acesso;
using Ministerio.Infra.Autenticacao;
using Ministerio.Infra.Facial;
using Ministerio.Infra.Persistencia;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Ministerio.Api.Endpoints;

/// <summary>O que vem junto de uma ação sensível: os dados dela, o desafio e a credencial (rosto ou senha).</summary>
internal sealed record Confirmacao(string? Dados, Guid DesafioId, string? Senha, byte[]? Imagem);

internal static class StepUpEndpoints
{
    public static IEndpointRouteBuilder MapearStepUp(this IEndpointRouteBuilder rotas)
    {
        rotas.MapGroup("/v1/biometria").RequireAuthorization().RequireRateLimiting(Limites.BiometriaAutenticada)
            .MapPost("/desafios", AbrirDesafio);
        return rotas;
    }

    // Abre o desafio de uma ação sensível. Pedir um nível acima do seu já é recusado aqui, antes de gastar o rosto, e fica no relatório.
    private static async Task<IResult> AbrirDesafio(
        DesafioDeAcaoRequest? corpo,
        HttpContext http,
        ClaimsPrincipal usuario,
        ServicoDeStepUp stepUp,
        MinisterioDbContext db,
        ServicoDeAdmins admins,
        ServicoDeAuditoria auditoria,
        IOptions<JsonOptions> json,
        CancellationToken cancelamento)
    {
        if (!Guid.TryParse(usuario.FindFirstValue(JwtRegisteredClaimNames.Sub), out var usuarioId))
        {
            return Results.Unauthorized();
        }

        FinalidadeDoDesafio finalidade;
        string instrucao;
        var canonico = AcoesSensiveis.SemParametros;
        Guid? cadastroAlvo = null;
        switch (corpo?.Finalidade)
        {
            case "ACESSO_NIVEL_2":
                (finalidade, instrucao) = (FinalidadeDoDesafio.AcessoNivel2, "Mostre o rosto para abrir as informações de nível 2.");
                break;
            case "ACESSO_NIVEL_3":
                (finalidade, instrucao) = (FinalidadeDoDesafio.AcessoNivel3, "Mostre o rosto para abrir as informações de nível 3.");
                break;
            case "TROCAR_SENHA":
                (finalidade, instrucao) = (FinalidadeDoDesafio.TrocarSenha, "Confirme com o rosto ou com a senha atual a troca de senha.");
                break;
            case "RECADASTRO":
                (finalidade, instrucao) = (FinalidadeDoDesafio.Recadastro, "Confirme com o rosto atual ou com a senha a troca do rosto.");
                break;
            case "ALTERAR_ACESSO":
                var parametros = corpo.Parametros is { ValueKind: JsonValueKind.Object } p ? p.GetRawText() : null;
                var pedido = Ler<AlterarAcessoRequest>(parametros, json.Value.SerializerOptions);
                var errosDoPedido = AcoesSensiveis.ErrosDoAcesso(pedido);
                if (errosDoPedido.Count > 0)
                {
                    return Validacao.Problema(errosDoPedido);
                }

                (finalidade, instrucao, canonico) = (FinalidadeDoDesafio.AlterarAcesso, "Mostre o rosto para confirmar a mudança de acesso.", AcoesSensiveis.Canonico(pedido!.UsuarioId!.Value, pedido.Nivel, pedido.Admin));
                break;
            case "ALTERAR_PROCESSAMENTO":
                var processamento = Ler<AlterarProcessamentoRequest>(corpo.Parametros is { ValueKind: JsonValueKind.Object } pp ? pp.GetRawText() : null, json.Value.SerializerOptions);
                var errosDoProcessamento = AcoesSensiveis.ErrosDoProcessamento(processamento);
                if (errosDoProcessamento.Count > 0)
                {
                    return Validacao.Problema(errosDoProcessamento);
                }

                (finalidade, instrucao, canonico) = (FinalidadeDoDesafio.AlterarProcessamento, "Mostre o rosto para confirmar a mudança do processamento facial.", AcoesSensiveis.Canonico(processamento!.PreProcessamento!, processamento.TratamentoRuido!));
                break;
            case "EXCLUIR_CADASTRO":
                var exclusao = Ler<ExcluirCadastroRequest>(corpo.Parametros is { ValueKind: JsonValueKind.Object } pe ? pe.GetRawText() : null, json.Value.SerializerOptions);
                if (exclusao?.UsuarioId is not { } alvoDaExclusao)
                {
                    return Validacao.Problema(new Dictionary<string, string[]> { ["usuario_id"] = ["Informe o usuário."] });
                }

                cadastroAlvo = alvoDaExclusao;
                (finalidade, instrucao, canonico) = (FinalidadeDoDesafio.ExcluirCadastro, "Confirme a exclusão do cadastro. Não dá para desfazer.", AcoesSensiveis.Canonico(alvoDaExclusao, null, null));
                break;
            default:
                return Validacao.Problema(new Dictionary<string, string[]> { ["finalidade"] = ["Use ACESSO_NIVEL_2, ACESSO_NIVEL_3, TROCAR_SENHA, RECADASTRO, ALTERAR_ACESSO, ALTERAR_PROCESSAMENTO ou EXCLUIR_CADASTRO."] });
        }

        if (finalidade is FinalidadeDoDesafio.AcessoNivel2 or FinalidadeDoDesafio.AcessoNivel3)
        {
            var exigido = finalidade == FinalidadeDoDesafio.AcessoNivel3 ? Niveis.Ministro : Niveis.Diretor;
            if ((await db.ObterAsync(admins, usuarioId, cancelamento)).Nivel < exigido)
            {
                await auditoria.RegistrarAsync(usuarioId, EventoDaConsulta(exigido), ResultadoDoAcesso.Recusado, "NIVEL_INSUFICIENTE", http.Connection.RemoteIpAddress);
                throw new NivelInsuficienteException(exigido);
            }
        }

        // Excluir a própria conta qualquer um pode; excluir a de outra pessoa, só admin.
        var soAdmin = finalidade is FinalidadeDoDesafio.AlterarAcesso or FinalidadeDoDesafio.AlterarProcessamento
            || (finalidade == FinalidadeDoDesafio.ExcluirCadastro && cadastroAlvo != usuarioId);
        if (soAdmin && !(await db.ObterAsync(admins, usuarioId, cancelamento)).Admin)
        {
            throw new NaoEhAdminException();
        }

        var desafio = await stepUp.AbrirAsync(usuarioId, finalidade, canonico, http.Connection.RemoteIpAddress, cancelamento);
        return Results.Json(new DesafioDeAcaoResponse(desafio.Id, Codigo.De(finalidade), desafio.ExpiraEm, instrucao), statusCode: StatusCodes.Status201Created);
    }

    internal static EventoDeAcesso EventoDaConsulta(int nivel) => nivel switch
    {
        Niveis.Ministro => EventoDeAcesso.ConsultaNivel3,
        Niveis.Diretor => EventoDeAcesso.ConsultaNivel2,
        _ => EventoDeAcesso.ConsultaNivel1,
    };

    internal static T? Ler<T>(string? texto, JsonSerializerOptions opcoes) where T : class
    {
        try
        {
            return string.IsNullOrWhiteSpace(texto) ? null : JsonSerializer.Deserialize<T>(texto, opcoes);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Lê a parte multipart de uma ação sensível. Nulo quando não há desafio (a rota então responde STEP_UP_REQUIRED).</summary>
    internal static async Task<(Confirmacao? Confirmacao, Dictionary<string, string[]> Erros)> LerConfirmacaoAsync(HttpRequest requisicao, CancellationToken cancelamento)
    {
        var erros = new Dictionary<string, string[]>();
        if (!requisicao.HasFormContentType)
        {
            return (null, erros);
        }

        var formulario = await Imagens.LerFormularioAsync(requisicao, cancelamento);
        if (!Guid.TryParse(formulario["desafio_id"], out var desafioId))
        {
            return (null, erros);
        }

        string? senha = formulario["senha"];
        var temImagem = formulario.Files.GetFile("image") is not null;
        if (temImagem == !string.IsNullOrEmpty(senha))
        {
            erros["credencial"] = ["Envie a foto (image) ou a senha, só um dos dois."];
        }
        else if (senha is { Length: > Validacao.SenhaMaxima })
        {
            erros["senha"] = ["Senha longa demais."];
        }

        return (new Confirmacao(formulario["dados"], desafioId, senha, temImagem ? Imagens.LerImagem(formulario) : null), erros);
    }
}

/// <summary>Canônico das ações sensíveis: sem parâmetros (só dono e finalidade) ou com o usuário-alvo e os valores pedidos.</summary>
internal static class AcoesSensiveis
{
    public const string SemParametros = "{}";

    public static Dictionary<string, string[]> ErrosDoAcesso(AlterarAcessoRequest? pedido)
    {
        var erros = new Dictionary<string, string[]>();
        if (pedido?.UsuarioId is null)
        {
            erros["usuario_id"] = ["Informe o usuário."];
        }

        if (pedido is { Nivel: null, Admin: null })
        {
            erros["corpo"] = ["Informe o nível, o perfil de admin ou os dois."];
        }
        else if (pedido?.Nivel is < Niveis.Servidor or > Niveis.Ministro)
        {
            erros["nivel"] = ["Use 1, 2 ou 3."];
        }

        return erros;
    }

    public static Dictionary<string, string[]> ErrosDoProcessamento(AlterarProcessamentoRequest? pedido)
    {
        var erros = new Dictionary<string, string[]>();
        if (!ProcessamentoFacial.PreProcessamentos.Contains(pedido?.PreProcessamento ?? string.Empty))
        {
            erros["pre_processamento"] = [$"Use {string.Join(", ", ProcessamentoFacial.PreProcessamentos)}."];
        }

        if (!ProcessamentoFacial.TratamentosDeRuido.Contains(pedido?.TratamentoRuido ?? string.Empty))
        {
            erros["tratamento_ruido"] = [$"Use {string.Join(", ", ProcessamentoFacial.TratamentosDeRuido)}."];
        }

        return erros;
    }

    public static string Canonico(string preProcessamento, string tratamentoRuido) =>
        JsonSerializer.Serialize(new SortedDictionary<string, string>(StringComparer.Ordinal) { ["pre_processamento"] = preProcessamento, ["tratamento_ruido"] = tratamentoRuido });

    /// <summary>JSON com as chaves em ordem e só o que foi pedido: é o que entra no hash do desafio, no pedido e na confirmação.</summary>
    public static string Canonico(Guid usuarioId, int? nivel, bool? admin)
    {
        var campos = new SortedDictionary<string, object>(StringComparer.Ordinal) { ["usuario_id"] = usuarioId.ToString() };
        if (nivel is { } n)
        {
            campos["nivel"] = n;
        }

        if (admin is { } a)
        {
            campos["admin"] = a;
        }

        return JsonSerializer.Serialize(campos);
    }
}
