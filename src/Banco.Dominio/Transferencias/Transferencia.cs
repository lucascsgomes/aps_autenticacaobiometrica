using System.Globalization;
using System.Security.Cryptography;
using Banco.Dominio.Contas;

namespace Banco.Dominio.Transferencias;

public enum TipoTransferencia
{
    Pix,
    Ted,
    CreditoInicial,
}

/// <summary>O nome PendenteBiometria ficou, mas vale para rosto ou senha.</summary>
public enum StatusTransferencia
{
    PendenteBiometria,
    Processando,
    Concluida,
    Falha,
    Expirada,
}

public enum MetodoConfirmacao
{
    Face,
    Senha,
}

public static class MotivoFalha
{
    public const string SaldoInsuficiente = "SALDO_INSUFICIENTE";
    public const string DestinoIndisponivel = "DESTINO_INDISPONIVEL";
    public const string BiometriaRecusada = "BIOMETRIA_RECUSADA";
    public const string SenhaRecusada = "SENHA_RECUSADA";
}

/// <summary>Transferência com máquina de estados. O banco impõe as mesmas transições por trigger.</summary>
public sealed class Transferencia
{
    public const int TamanhoMaximoDescricao = 140;

    private const string AlfabetoE2e = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    public Guid Id { get; private set; }
    public TipoTransferencia Tipo { get; private set; }
    public Guid ContaOrigemId { get; private set; }
    public Guid ContaDestinoId { get; private set; }
    public long ValorCentavos { get; private set; }
    public string? Descricao { get; private set; }
    public StatusTransferencia Status { get; private set; }
    public string? MotivoFalha { get; private set; }

    /// <summary>Identificador de ponta a ponta, no formato do E2E ID do Pix. Só existe quando concluída.</summary>
    public string? E2eId { get; private set; }

    public MetodoConfirmacao? MetodoConfirmacao { get; private set; }
    public DateTimeOffset CriadaEm { get; private set; }
    public DateTimeOffset? ConcluidaEm { get; private set; }

    public Dinheiro Valor => Dinheiro.DeCentavos(ValorCentavos);

    private Transferencia()
    {
    }

    public static Transferencia Criar(Guid origemId, Guid destinoId, Dinheiro valor, string? descricao, DateTimeOffset agora, TipoTransferencia tipo = TipoTransferencia.Pix)
    {
        if (origemId == destinoId)
        {
            throw new ArgumentException("Origem e destino devem ser contas diferentes.", nameof(destinoId));
        }

        if (valor.Centavos <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(valor), "Valor da transferência deve ser maior que zero.");
        }

        if (descricao is { Length: > TamanhoMaximoDescricao })
        {
            throw new ArgumentException($"Descrição aceita até {TamanhoMaximoDescricao} caracteres.", nameof(descricao));
        }

        return new Transferencia
        {
            Id = Guid.NewGuid(),
            Tipo = tipo,
            ContaOrigemId = origemId,
            ContaDestinoId = destinoId,
            ValorCentavos = valor.Centavos,
            Descricao = descricao,
            Status = StatusTransferencia.PendenteBiometria,
            CriadaEm = agora,
        };
    }

    /// <summary>Saldo fictício inicial: nasce concluída, sem passar por confirmação.</summary>
    public static Transferencia CreditoInicial(Guid contaSistemaId, Guid destinoId, Dinheiro valor, DateTimeOffset agora)
    {
        var t = Criar(contaSistemaId, destinoId, valor, "Crédito inicial", agora, TipoTransferencia.CreditoInicial);
        t.Status = StatusTransferencia.Concluida;
        t.ConcluidaEm = agora;
        t.E2eId = GerarE2eId(agora);
        return t;
    }

    public void Confirmar(MetodoConfirmacao metodo)
    {
        Mover(StatusTransferencia.Processando);
        MetodoConfirmacao = metodo;
    }

    public void Expirar() => Mover(StatusTransferencia.Expirada);

    /// <summary>Vale de PendenteBiometria (recusa) e de Processando (saldo ou destino).</summary>
    public void Falhar(string motivo)
    {
        if (string.IsNullOrWhiteSpace(motivo))
        {
            throw new ArgumentException("Falha exige um motivo.", nameof(motivo));
        }

        Mover(StatusTransferencia.Falha);
        MotivoFalha = motivo;
    }

    public void Concluir(DateTimeOffset agora)
    {
        Mover(StatusTransferencia.Concluida);
        ConcluidaEm = agora;
        E2eId = GerarE2eId(agora);
    }

    private void Mover(StatusTransferencia para)
    {
        var valida = (Status, para) switch
        {
            (StatusTransferencia.PendenteBiometria, StatusTransferencia.Processando or StatusTransferencia.Falha or StatusTransferencia.Expirada) => true,
            (StatusTransferencia.Processando, StatusTransferencia.Concluida or StatusTransferencia.Falha) => true,
            _ => false,
        };

        Status = valida ? para : throw new InvalidOperationException($"Transição inválida de {Status} para {para}.");
    }

    // E + 8 dígitos (ISPB fictício) + data e hora UTC (12 dígitos) + 11 alfanuméricos.
    private static string GerarE2eId(DateTimeOffset agora) =>
        "E00000000" + agora.UtcDateTime.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture) + RandomNumberGenerator.GetString(AlfabetoE2e, 11);
}
