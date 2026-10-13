using Banco.Api.Seguranca;
using System.Security.Claims;
using Banco.Contratos;
using Banco.Dominio;
using Banco.Infra.Facial;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.JsonWebTokens;
using Banco.Infra.Autenticacao;
using Banco.Infra.Cadastro;
using Banco.Dominio.Usuarios;

namespace Banco.Api.Endpoints;

internal static class AuthEndpoints
{
    private const string MetodoSenha = "SENHA";
    private const string MetodoFace = "FACE";
    private const int TamanhoMaximoDaImagem = 2 * 1024 * 1024;
    private const int TamanhoMaximoDaRequisicao = TamanhoMaximoDaImagem + 64 * 1024;
    private const string CapturaFacial = "CAPTURA_FACIAL";

    public static IEndpointRouteBuilder MapearAuth(this IEndpointRouteBuilder rotas)
    {
        var auth = rotas.MapGroup("/v1/auth");
        auth.MapPost("/register", Registrar).RequireRateLimiting(Limites.Publico);
        auth.MapPost("/register/retomar", Retomar).RequireRateLimiting(Limites.Biometria);
        auth.MapPost("/login/desafio", AbrirDesafioDeLogin).RequireRateLimiting(Limites.Biometria);
        auth.MapPost("/login/face", LoginComRosto)
            .RequireRateLimiting(Limites.Biometria)
            .WithMetadata(new RequestSizeLimitAttribute(TamanhoMaximoDaRequisicao));
        auth.MapPost("/login/senha", LoginComSenha).RequireRateLimiting(Limites.Biometria);
        auth.MapPost("/face/enroll", CadastrarRosto)
            .RequireAuthorization(JwtOpcoes.PoliticaDeCadastro)
            .RequireRateLimiting(Limites.Biometria)
            .WithMetadata(new RequestSizeLimitAttribute(TamanhoMaximoDaRequisicao));
        auth.MapPost("/refresh", Renovar).RequireRateLimiting(Limites.Publico);
        auth.MapPost("/logout", Sair).RequireRateLimiting(Limites.Publico);
        return rotas;
    }

    private static async Task<IResult> Registrar(
        RegistrarRequest? corpo,
        ServicoDeCadastro cadastro,
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

        // O cadastro sem rosto só existe em Development: em produção a conta só ativa quando a biometria entra.
        var semRosto = ambiente.IsDevelopment() && configuracao.GetValue<bool>("Auth:CadastroSemRosto");
        var criado = await cadastro.CadastrarAsync(
            new NovoCadastro(corpo!.Nome!, corpo.Cpf!, corpo.Email!, corpo.Celular, corpo.Senha!, corpo.ConsentimentoBiometrico!.VersaoTermo!),
            semRosto,
            cancelamento);

        var sessao = criado.RefreshToken is null
            ? null
            : MontarSessao(new SessaoEmitida(criado.UsuarioId, criado.Nome, criado.ContaId, criado.RefreshToken), jwt, null);
        var pendente = criado.Status == StatusUsuario.CadastroPendente;
        var cadastroToken = pendente ? jwt.EmitirDeCadastro(criado.UsuarioId) : default;
        var resposta = new RegistrarResponse(
            criado.UsuarioId,
            new ContaResumoDto(criado.ContaId, criado.Agencia, criado.Numero),
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
        JwtEmissor jwt,
        CancellationToken cancelamento)
    {
        if (!Guid.TryParse(usuario.FindFirstValue(JwtRegisteredClaimNames.Sub), out var usuarioId))
        {
            return Results.Unauthorized();
        }

        var imagem = LerImagem(await LerFormularioAsync(http.Request, cancelamento));
        var embedding = await facial.CadastrarAsync(imagem, http.TraceIdentifier, cancelamento);
        var sessao = await cadastro.ConcluirComRostoAsync(usuarioId, embedding, cancelamento);
        return Results.Json(MontarSessao(sessao, jwt, MetodoFace), statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IFormCollection> LerFormularioAsync(HttpRequest requisicao, CancellationToken cancelamento) =>
        requisicao.HasFormContentType ? await requisicao.ReadFormAsync(cancelamento) : throw new ImagemInvalidaException();

    private static byte[] LerImagem(IFormCollection formulario)
    {
        if (formulario.Files.GetFile("image") is not { Length: > 0 } arquivo)
        {
            throw new ImagemInvalidaException();
        }

        if (arquivo.Length > TamanhoMaximoDaImagem)
        {
            throw new ImagemGrandeException();
        }

        using var fluxo = arquivo.OpenReadStream();
        using var memoria = new MemoryStream((int)arquivo.Length);
        fluxo.CopyTo(memoria);
        return memoria.ToArray();
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
    private static async Task<IResult> LoginComRosto(HttpContext http, ServicoDeLoginFacial login, JwtEmissor jwt, CancellationToken cancelamento)
    {
        var formulario = await LerFormularioAsync(http.Request, cancelamento);
        if (!Guid.TryParse(formulario["desafio_id"], out var desafioId))
        {
            return Validacao.Problema(new Dictionary<string, string[]> { ["desafio_id"] = ["Informe o desafio recebido em login/desafio."] });
        }

        var imagem = LerImagem(formulario);
        var sessao = await login.EntrarAsync(desafioId, imagem, http.TraceIdentifier, cancelamento);
        return Results.Ok(MontarSessao(sessao, jwt, MetodoFace));
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
            new RegistrarResponse(posse.UsuarioId, new ContaResumoDto(posse.ContaId, posse.Agencia, posse.Numero), CapturaFacial, null, recomecado.Token, recomecado.ExpiraEm),
            statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> LoginComSenha(
        LoginSenhaRequest? corpo,
        ServicoDeAutenticacao autenticacao,
        JwtEmissor jwt,
        CancellationToken cancelamento)
    {
        var erros = Validacao.Login(corpo);
        if (erros.Count > 0)
        {
            return Validacao.Problema(erros);
        }

        var sessao = await autenticacao.LoginComSenhaAsync(corpo!.Cpf!, corpo.Senha!, cancelamento);
        return Results.Ok(MontarSessao(sessao, jwt, MetodoSenha));
    }

    private static async Task<IResult> Renovar(RefreshRequest? corpo, ServicoDeAutenticacao autenticacao, JwtEmissor jwt, CancellationToken cancelamento)
    {
        if (string.IsNullOrWhiteSpace(corpo?.RefreshToken))
        {
            throw new Dominio.NaoAutenticadoException();
        }

        var sessao = await autenticacao.RenovarAsync(corpo.RefreshToken, cancelamento);
        return Results.Ok(MontarSessao(sessao, jwt, null));
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

    private static SessaoResponse MontarSessao(SessaoEmitida sessao, JwtEmissor jwt, string? metodo) => new(
        "Bearer",
        jwt.Emitir(sessao.UsuarioId, sessao.ContaId),
        jwt.ExpiraEmSegundos,
        sessao.RefreshToken,
        (int)RefreshTokenServico.Validade.TotalSeconds,
        new UsuarioDto(sessao.UsuarioId, sessao.Nome),
        metodo);
}
