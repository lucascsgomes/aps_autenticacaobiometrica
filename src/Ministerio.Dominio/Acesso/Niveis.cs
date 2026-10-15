using Ministerio.Dominio.Biometria;

namespace Ministerio.Dominio.Acesso;

/// <summary>Os três níveis de acesso à base do Ministério do Meio Ambiente.</summary>
public static class Niveis
{
    /// <summary>Nível 1: todos os servidores cadastrados.</summary>
    public const int Servidor = 1;

    /// <summary>Nível 2: diretores de divisão.</summary>
    public const int Diretor = 2;

    /// <summary>Nível 3: somente o ministro.</summary>
    public const int Ministro = 3;

    public static string Cargo(int nivel) => nivel switch
    {
        Ministro => "Ministro do Meio Ambiente",
        Diretor => "Diretor de divisão",
        _ => "Servidor",
    };

    /// <summary>Só os níveis restritos têm desafio de rosto; o nível 1 abre só com a sessão.</summary>
    public static FinalidadeDoDesafio? FinalidadeDoAcesso(int nivel) => nivel switch
    {
        Diretor => FinalidadeDoDesafio.AcessoNivel2,
        Ministro => FinalidadeDoDesafio.AcessoNivel3,
        _ => null,
    };
}
