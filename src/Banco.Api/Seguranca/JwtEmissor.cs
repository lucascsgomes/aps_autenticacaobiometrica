using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Banco.Api.Seguranca;

/// <summary>Configuração do JWT. A chave nunca vem do repositório: user-secrets, variável de ambiente ou cofre.</summary>
internal sealed class JwtOpcoes
{
    public const string EscopoSessao = "sessao";

    public string Emissor { get; init; } = "banco-api";
    public string Audiencia { get; init; } = "banco-clientes";
    public TimeSpan Validade { get; init; } = TimeSpan.FromMinutes(15);
    public required SymmetricSecurityKey Chave { get; init; }

    /// <summary>Fora de Development a chave é obrigatória. Em Development, sem chave, sorteia uma (os tokens morrem ao reiniciar).</summary>
    public static JwtOpcoes Carregar(IConfiguration configuracao, IHostEnvironment ambiente)
    {
        var texto = configuracao["Jwt:Chave"];
        byte[] bytes;
        if (string.IsNullOrWhiteSpace(texto))
        {
            bytes = ambiente.IsDevelopment()
                ? RandomNumberGenerator.GetBytes(32)
                : throw new InvalidOperationException("Configure Jwt:Chave (mínimo de 32 caracteres) fora do ambiente de desenvolvimento.");
        }
        else
        {
            bytes = Encoding.UTF8.GetBytes(texto);
            if (bytes.Length < 32)
            {
                throw new InvalidOperationException("Jwt:Chave precisa ter pelo menos 32 bytes (HS256).");
            }
        }

        return new JwtOpcoes
        {
            Emissor = configuracao["Jwt:Emissor"] ?? "banco-api",
            Audiencia = configuracao["Jwt:Audiencia"] ?? "banco-clientes",
            Chave = new SymmetricSecurityKey(bytes),
        };
    }
}

internal sealed class JwtEmissor(JwtOpcoes opcoes, TimeProvider relogio)
{
    private readonly JsonWebTokenHandler _handler = new();

    public int ExpiraEmSegundos => (int)opcoes.Validade.TotalSeconds;

    /// <summary>Claims: sub (usuário), cid (conta), esc (escopo), jti, iat e exp. Algoritmo fixo HS256.</summary>
    public string Emitir(Guid usuarioId, Guid contaId)
    {
        var agora = relogio.GetUtcNow().UtcDateTime;
        return _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = opcoes.Emissor,
            Audience = opcoes.Audiencia,
            IssuedAt = agora,
            NotBefore = agora,
            Expires = agora + opcoes.Validade,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = usuarioId.ToString(),
                ["cid"] = contaId.ToString(),
                ["esc"] = JwtOpcoes.EscopoSessao,
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
            },
            SigningCredentials = new SigningCredentials(opcoes.Chave, SecurityAlgorithms.HmacSha256),
        });
    }

    public TokenValidationParameters ParametrosDeValidacao() => new()
    {
        ValidateIssuer = true,
        ValidIssuer = opcoes.Emissor,
        ValidateAudience = true,
        ValidAudience = opcoes.Audiencia,
        ValidateLifetime = true,
        RequireExpirationTime = true,
        RequireSignedTokens = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = opcoes.Chave,
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        ClockSkew = TimeSpan.FromSeconds(30),
    };
}
