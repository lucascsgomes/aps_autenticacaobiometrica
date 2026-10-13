using Banco.Dominio.Chaves;

namespace Banco.Tests;

public class ChavesTests
{
    [Theory]
    [InlineData("52998224725", TipoChave.Cpf)]
    [InlineData("+5511987654321", TipoChave.Celular)]
    [InlineData("maria@exemplo.com", TipoChave.Email)]
    [InlineData("3f2b8c1e-5a4d-4e6f-9a1b-2c3d4e5f6a7b", TipoChave.Aleatoria)]
    public void Reconhece_o_tipo_pelo_formato(string valor, TipoChave esperado) => Assert.Equal(esperado, ChaveTransferencia.Classificar(valor));

    [Theory]
    [InlineData("529.982.247-25")] // pontuação
    [InlineData("52998224724")] // dígito verificador
    [InlineData("Maria@Exemplo.com")] // maiúscula
    [InlineData("11987654321")] // celular sem +55 é CPF inválido
    [InlineData("3f2b8c1e-5a4d-1e6f-9a1b-2c3d4e5f6a7b")] // não é UUID v4
    [InlineData("")]
    [InlineData(null)]
    public void Recusa_formato_invalido(string? valor) => Assert.Null(ChaveTransferencia.Classificar(valor));

    [Fact]
    public void Criar_exige_que_o_valor_combine_com_o_tipo() =>
        Assert.Throws<ArgumentException>(() => ChaveTransferencia.Criar(Guid.NewGuid(), TipoChave.Email, "52998224725", DateTimeOffset.UtcNow));

    [Fact]
    public void Mascara_esconde_o_que_nao_deve_aparecer()
    {
        Assert.Equal("Maria S***", Mascara.Nome("Maria de Souza"));
        Assert.Equal("Maria***", Mascara.Nome("Maria"));
        Assert.Equal("***.982.247-**", Mascara.Chave(TipoChave.Cpf, "52998224725"));
        Assert.Equal("m***@exemplo.com", Mascara.Chave(TipoChave.Email, "maria@exemplo.com"));
        Assert.Equal("+55*******4321", Mascara.Chave(TipoChave.Celular, "+5511987654321"));
        Assert.Equal("3f2b8c1e-****", Mascara.Chave(TipoChave.Aleatoria, "3f2b8c1e-5a4d-4e6f-9a1b-2c3d4e5f6a7b"));
        Assert.Equal("****-3", Mascara.Conta("963263"));
    }
}
