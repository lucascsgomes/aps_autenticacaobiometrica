using System.Security.Claims;
using Ministerio.Api.Seguranca;
using Ministerio.Contratos;
using Ministerio.Dominio;
using Ministerio.Dominio.Acesso;
using Ministerio.Dominio.Usuarios;
using Ministerio.Infra.Acesso;
using Ministerio.Infra.Autenticacao;
using Ministerio.Infra.Cadastro;
using Ministerio.Infra.Facial;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Ministerio.Api.Endpoints;

internal static class AuthEndpoints
{
    private const string MetodoSenha = "SENHA";
    private const string MetodoFace = "FACE";
    private const string MetodoIdentificacao = "IDENTIFICACAO";
    private const string CapturaFacial = "CAPTURA_FACIAL";

    public static IEndpointRouteBuilder MapearAuth(this IEndpointRouteBuilder rotas)
    {
        var auth = rotas.MapGroup("/v1/auth");
        auth.MapPost("/register", Registrar).RequireRateLimiting(Limites.Publico);
        auth.MapPost("/register/retomar", Retomar).RequireRateLimiting(Limites.Biometria);
        auth.MapPost("/login/desafio", AbrirDesafioDeLogin).RequireRateLimiting(Limites.Biometria);
        auth.MapPost("/login/face", LoginComRosto)
            .RequireRateLimiting(Limites.Biometria)
            .WithMetadata(new RequestSizeLimitAttribute(Imagens.TamanhoMaximoDaRequisicao));
        auth.MapPost("/login/identificar", LoginPorIdentificacao)
            .RequireRateLimiting(Limites.Biometria)
            .WithMetadata(new RequestSizeLimitAttribute(Imagens.TamanhoMaximoDaRequisicao));
        auth.MapPost("/login/senha", LoginComSenha).RequireRateLimiting(Limites.Biometria);
        auth.MapPost("/face/enroll", CadastrarRosto)
            .RequireAuthorization(JwtOpcoes.PoliticaDeCadastro)
            .RequireRateLimiting(Limites.Biometria)
            .WithMetadata(new RequestSizeLimitAttribute(Imagens.TamanhoMaximoDaRequisicao));
        auth.MapPost("/refresh", Renovar).RequireRateLimiting(Limites.Publico);
        auth.MapPost("/logout", Sair).RequireRateLimiting(Limites.Publico);
        return rotas;
    }

    private static async Task<IResult> Registrar(
        RegistrarRequest? corpo,
        ServicoDeCadastro cadastro,
        ServicoDeNiveis niveis,
        JwtEmissor jwt,
        IConfiguration configuracao,
        IHostEnvironment ambiente,
        CancellationToken cancelamento)
    {
        var erros = Validacao.Cadastro(corpo);
        if (erros.Count > 0)
        {
            return Validacao.Problema(erros);
        }

        // O cadastro sem rosto só existe em Development: em produção o cadastro só termina quando a biometria entra.
        var semRosto = ambiente.IsDevelopment() && configuracao.GetValue<bool>("Auth:CadastroSemRosto");
        var criado = await cadastro.CadastrarAsync(
            new NovoCadastro(corpo!.Nome!, corpo.Cpf!, corpo.Email!, corpo.Celular, corpo.Senha!, corpo.ConsentimentoBiometrico!.VersaoTermo!),
            semRosto,
            cancelamento);

        var sessao = criado.RefreshToken is null
            ? null
            : MontarSessao(new SessaoEmitida(criado.UsuarioId, criado.Nome, corpo.Cpf!, criado.RefreshToken), jwt, niveis, null);
        var pendente = criado.Status == StatusUsuario.CadastroPendente;
        var cadastroToken = pendente ? jwt.EmitirDeCadastro(criado.UsuarioId) : default;
        var resposta = new RegistrarResponse(
            criado.UsuarioId,
            pendente ? CapturaFacial : null,
            sessao,
            pendente ? cadastroToken.Token : null,
            pendente ? cadastroToken.ExpiraEm : null);
        return Results.Json(resposta, statusCode: StatusCodes.Status201Created);
    }

    // O rosto só entra aqui: a foto (até 2 MB) vai ao face-service, o vetor volta, é cifrado e guardado, e o cadastro conclui.
    // Foto sem rosto ou com vários não gasta o cadastro: o usuário tenta de novo até o cadastro_token vencer.
    private static async Task<IResult> CadastrarRosto(
        HttpContext http,
        ClaimsPrincipal usuario,
        IClienteFacial facial,
        ServicoDeCadastro cadastro,
        ServicoDeNiveis niveis,
        JwtEmissor jwt,
        CancellationToken cancelamento)
    {
        if (!Guid.TryParse(usuario.FindFirstValue(JwtRegisteredClaimNames.Sub), out var usuarioId))
        {
            return Results.Unauthorized();
        }

        var imagem = Imagens.LerImagem(await Imagens.LerFormularioAsync(http.Request, cancelamento));
        var embedding = await facial.CadastrarAsync(imagem, http.TraceIdentifier, cancelamento);
        var sessao = await cadastro.ConcluirComRostoAsync(usuarioId, embedding, cancelamento);
        return Results.Json(MontarSessao(sessao, jwt, niveis, MetodoFace), statusCode: StatusCodes.Status201Created);
    }

    // Responde 200 com a mesma forma exista o CPF ou não: quem consulta não descobre quem tem conta nem quem tem rosto.
    private static async Task<IResult> AbrirDesafioDeLogin(LoginDesafioRequest? corpo, HttpContext http, ServicoDeLoginFacial login, CancellationToken cancelamento)
    {
        var erros = Validacao.SoCpf(corpo?.Cpf);
        if (erros.Count > 0)
        {
            return Validacao.Problema(erros);
        }

        var desafio = await login.AbrirDesafioAsync(corpo!.Cpf!, http.Connection.RemoteIpAddress, cancelamento);
        return Results.Ok(new DesafioResponse(desafio.Id, desafio.ExpiraEm, "Posicione o rosto na moldura e toque em Entrar."));
    }

    // Foto sem rosto ou com vários (422) devolve o desafio: dá para tirar outra foto com o mesmo desafio_id, até 5 vezes.
    private static async Task<IResult> LoginComRosto(
        HttpContext http,
        ServicoDeLoginFacial login,
        ServicoDeAuditoria auditoria,
        ServicoDeNiveis niveis,
        JwtEmissor jwt,
        CancellationToken cancelamento)
    {
        var formulario = await Imagens.LerFormularioAsync(http.Request, cancelamento);
        if (!Guid.TryParse(formulario["desafio_id"], out var desafioId))
        {
            return Validacao.Problema(new Dictionary<string, string[]> { ["desafio_id"] = ["Informe o desafio recebido em login/desafio."] });
        }

        var imagem = Imagens.LerImagem(formulario);
        var ip = http.Connection.RemoteIpAddress;
        try
        {
            var sessao = await login.EntrarAsync(desafioId, imagem, http.TraceIdentifier, cancelamento);
            await auditoria.RegistrarAsync(sessao.UsuarioId, EventoDeAcesso.LoginRosto, ResultadoDoAcesso.Aceito, null, ip);
            return Results.Ok(MontarSessao(sessao, jwt, niveis, MetodoFace));
        }
        catch (NegocioException e)
        {
            await auditoria.RecusarPorDesafioAsync(desafioId, EventoDeAcesso.LoginRosto, e.Code, ip);
            throw;
        }
    }

    // Sem CPF: o face-service compara a foto com todos os rostos cadastrados e diz quem é (1 para N).
    private static async Task<IResult> LoginPorIdentificacao(
        HttpContext http,
        ServicoDeIdentificacao identificacao,
        ServicoDeAuditoria auditoria,
        ServicoDeNiveis niveis,
        JwtEmissor jwt,
        CancellationToken cancelamento)
    {
        var imagem = Imagens.LerImagem(await Imagens.LerFormularioAsync(http.Request, cancelamento));
        var ip = http.Connection.RemoteIpAddress;
        try
        {
            var sessao = await identificacao.EntrarAsync(imagem, http.TraceIdentifier, cancelamento);
            await auditoria.RegistrarAsync(sessao.UsuarioId, EventoDeAcesso.LoginIdentificacao, ResultadoDoAcesso.Aceito, null, ip);
            return Results.Ok(MontarSessao(sessao, jwt, niveis, MetodoIdentificacao));
        }
        catch (NegocioException e)
        {
            await auditoria.RegistrarAsync(null, EventoDeAcesso.LoginIdentificacao, ResultadoDoAcesso.Recusado, e.Code, ip);
            throw;
        }
    }

    private static async Task<IResult> Retomar(RetomarRequest? corpo, ServicoDeCadastro cadastro, JwtEmissor jwt, CancellationToken cancelamento)
    {
        var erros = Validacao.Retomar(corpo);
        if (erros.Count > 0)
        {
            return Validacao.Problema(erros);
        }

        var posse = await cadastro.ProvarPosseAsync(corpo!.Cpf!, corpo.Senha!, cancelamento);
        if (corpo.Acao == "RETOMAR")
        {
            var retomado = jwt.EmitirDeCadastro(posse.UsuarioId);
            return Results.Ok(new RetomarResponse(retomado.Token, retomado.ExpiraEm, CapturaFacial));
        }

        var d = corpo.Dados!;
        await cadastro.RecomecarAsync(posse.UsuarioId, new NovoCadastro(d.Nome!, corpo.Cpf!, d.Email!, d.Celular, d.Senha!, d.ConsentimentoBiometrico!.VersaoTermo!), cancelamento);
        var recomecado = jwt.EmitirDeCadastro(posse.UsuarioId);
        return Results.Json(
            new RegistrarResponse(posse.UsuarioId, CapturaFacial, null, recomecado.Token, recomecado.ExpiraEm),
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> LoginComSenha(
        LoginSenhaRequest? corpo,
        HttpContext http,
        ServicoDeAutenticacao autenticacao,
        ServicoDeAuditoria auditoria,
        ServicoDeNiveis niveis,
        JwtEmissor jwt,
        CancellationToken cancelamento)
    {
        var erros = Validacao.Login(corpo);
        if (erros.Count > 0)
        {
            return Validacao.Problema(erros);
        }

        var ip = http.Connection.RemoteIpAddress;
        try
        {
            var sessao = await autenticacao.LoginComSenhaAsync(corpo!.Cpf!, corpo.Senha!, cancelamento);
            await auditoria.RegistrarAsync(sessao.UsuarioId, EventoDeAcesso.LoginSenha, ResultadoDoAcesso.Aceito, null, ip);
            return Results.Ok(MontarSessao(sessao, jwt, niveis, MetodoSenha));
        }
        catch (NegocioException e)
        {
            await auditoria.RecusarPorCpfAsync(corpo!.Cpf, EventoDeAcesso.LoginSenha, e.Code, ip);
            throw;
        }
    }

    private static async Task<IResult> Renovar(RefreshRequest? corpo, ServicoDeAutenticacao autenticacao, ServicoDeNiveis niveis, JwtEmissor jwt, CancellationToken cancelamento)
    {
        if (string.IsNullOrWhiteSpace(corpo?.RefreshToken))
        {
            throw new NaoAutenticadoException();
        }

        var sessao = await autenticacao.RenovarAsync(corpo.RefreshToken, cancelamento);
        return Results.Ok(MontarSessao(sessao, jwt, niveis, null));
    }

    // Sem exigir o JWT: quem tem o refresh token prova a posse da sessão, e o access token pode já ter vencido.
    private static async Task<IResult> Sair(RefreshRequest? corpo, ServicoDeAutenticacao autenticacao, CancellationToken cancelamento)
    {
        if (!string.IsNullOrWhiteSpace(corpo?.RefreshToken))
        {
            await autenticacao.SairAsync(corpo.RefreshToken, cancelamento);
        }

        return Results.NoContent();
    }

    private static SessaoResponse MontarSessao(SessaoEmitida sessao, JwtEmissor jwt, ServicoDeNiveis niveis, string? metodo)
    {
        var nivel = niveis.De(sessao.Cpf);
        return new SessaoResponse(
            "Bearer",
            jwt.Emitir(sessao.UsuarioId),
            jwt.ExpiraEmSegundos,
            sessao.RefreshToken,
            (int)RefreshTokenServico.Validade.TotalSeconds,
            new UsuarioDto(sessao.UsuarioId, sessao.Nome, nivel, Niveis.Cargo(nivel)),
            metodo);
    }
}
