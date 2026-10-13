using Banco.Api.Seguranca;
using Banco.Contratos;
using Banco.Infra.Autenticacao;
using Banco.Infra.Cadastro;
using Banco.Dominio.Usuarios;

namespace Banco.Api.Endpoints;

internal static class AuthEndpoints
{
    private const string MetodoSenha = "SENHA";
    private const string CapturaFacial = "CAPTURA_FACIAL";

    public static IEndpointRouteBuilder MapearAuth(this IEndpointRouteBuilder rotas)
    {
        var auth = rotas.MapGroup("/v1/auth");
        auth.MapPost("/register", Registrar).RequireRateLimiting(Limites.Publico);
        auth.MapPost("/login/senha", LoginComSenha).RequireRateLimiting(Limites.Biometria);
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
        var resposta = new RegistrarResponse(
            criado.UsuarioId,
            new ContaResumoDto(criado.ContaId, criado.Agencia, criado.Numero),
            criado.Status == StatusUsuario.CadastroPendente ? CapturaFacial : null,
            sessao);
        return Results.Json(resposta, statusCode: StatusCodes.Status201Created);
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
