using Banco.Dominio;
using Banco.Dominio.Biometria;
using Banco.Dominio.Contas;
using Banco.Dominio.Transferencias;

namespace Banco.Tests;

public class DesafioDeTransferenciaTests
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    private static Transferencia Nova(long centavos = 10_000) =>
        Transferencia.Criar(Guid.NewGuid(), Guid.NewGuid(), Dinheiro.DeCentavos(centavos), null, Agora);

    [Fact]
    public void Hash_amarra_usuario_e_transferencia_e_e_estavel()
    {
        var t = Nova();
        var usuario = Guid.NewGuid();

        Assert.Equal(DesafioBiometrico.HashDaTransferencia(usuario, t), DesafioBiometrico.HashDaTransferencia(usuario, t));
        Assert.NotEqual(DesafioBiometrico.HashDaTransferencia(usuario, t), DesafioBiometrico.HashDaTransferencia(Guid.NewGuid(), t));
        Assert.NotEqual(DesafioBiometrico.HashDaTransferencia(usuario, t), DesafioBiometrico.HashDaTransferencia(usuario, Nova()));
    }

    [Fact]
    public void Desafio_de_transferencia_nasce_com_tudo_que_o_banco_exige()
    {
        var t = Nova(25_000);
        var usuario = Guid.NewGuid();

        var d = DesafioBiometrico.AbrirDeTransferencia(usuario, t, 0.450m, null, Agora);

        Assert.Equal(FinalidadeDoDesafio.Transferencia, d.Finalidade);
        Assert.Equal(t.Id, d.TransferenciaId);
        Assert.Equal(25_000, d.ValorCentavos);
        Assert.Equal(t.ContaDestinoId, d.ContaDestinoId);
        Assert.Equal(DesafioBiometrico.HashDaTransferencia(usuario, t), d.HashOperacao);
        Assert.Equal(Agora + DesafioBiometrico.Validade, d.ExpiraEm);
    }

    [Fact]
    public void Hash_da_acao_muda_com_o_dono_a_finalidade_os_parametros_e_o_nonce()
    {
        var usuario = Guid.NewGuid();
        var nonce = new byte[32];
        var basico = DesafioBiometrico.HashDaAcao(usuario, FinalidadeDoDesafio.AlterarLimite, """{"limite_diurno_centavos":450000}""", nonce);

        Assert.Equal(basico, DesafioBiometrico.HashDaAcao(usuario, FinalidadeDoDesafio.AlterarLimite, """{"limite_diurno_centavos":450000}""", nonce));
        Assert.NotEqual(basico, DesafioBiometrico.HashDaAcao(Guid.NewGuid(), FinalidadeDoDesafio.AlterarLimite, """{"limite_diurno_centavos":450000}""", nonce));
        Assert.NotEqual(basico, DesafioBiometrico.HashDaAcao(usuario, FinalidadeDoDesafio.NovaChave, """{"limite_diurno_centavos":450000}""", nonce));
        Assert.NotEqual(basico, DesafioBiometrico.HashDaAcao(usuario, FinalidadeDoDesafio.AlterarLimite, """{"limite_diurno_centavos":500000}""", nonce));
        Assert.NotEqual(basico, DesafioBiometrico.HashDaAcao(usuario, FinalidadeDoDesafio.AlterarLimite, """{"limite_diurno_centavos":450000}""", new byte[32].Select(_ => (byte)1).ToArray()));
    }
}
