using Banco.Dominio.Contas;
using Banco.Dominio.Transferencias;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Banco.Infra.Persistencia.Configurations;

internal sealed class TransferenciaConfiguration : IEntityTypeConfiguration<Transferencia>
{
    public void Configure(EntityTypeBuilder<Transferencia> t)
    {
        t.ToTable("transferencias", c =>
        {
            c.HasCheckConstraint("ck_transferencias_tipo", "tipo IN ('PIX', 'TED', 'CREDITO_INICIAL')");
            c.HasCheckConstraint("ck_transferencias_valor", "valor_centavos > 0");
            c.HasCheckConstraint("ck_transferencias_descricao", "length(descricao) <= 140");
            c.HasCheckConstraint("ck_transferencias_status",
                "status IN ('PENDENTE_BIOMETRIA', 'PROCESSANDO', 'CONCLUIDA', 'FALHA', 'EXPIRADA')");
            c.HasCheckConstraint("ck_transferencias_e2e", "e2e_id ~ '^E[0-9]{8}[0-9]{12}[A-Za-z0-9]{11}$'");
            c.HasCheckConstraint("ck_transferencias_metodo", "metodo_confirmacao IN ('FACE', 'SENHA')");
            c.HasCheckConstraint("ck_contas_diferentes", "conta_origem_id <> conta_destino_id");
            c.HasCheckConstraint("ck_falha_tem_motivo", "(status = 'FALHA') = (motivo_falha IS NOT NULL)");
            c.HasCheckConstraint("ck_conclusao", "(status = 'CONCLUIDA') = (concluida_em IS NOT NULL)");
        });

        t.HasKey(x => x.Id);
        t.Ignore(x => x.Valor);
        t.Property(x => x.Tipo).HasConversion(new EnumComoTexto<TipoTransferencia>()).HasDefaultValue(TipoTransferencia.Pix).IsRequired();
        t.Property(x => x.Status).HasConversion(new EnumComoTexto<StatusTransferencia>()).IsRequired();
        t.Property(x => x.MetodoConfirmacao).HasConversion(new EnumComoTexto<MetodoConfirmacao>());

        t.HasOne<Conta>().WithMany().HasForeignKey(x => x.ContaOrigemId).OnDelete(DeleteBehavior.Restrict);
        t.HasOne<Conta>().WithMany().HasForeignKey(x => x.ContaDestinoId).OnDelete(DeleteBehavior.Restrict);

        t.HasIndex(x => x.E2eId).IsUnique().HasDatabaseName("uq_transferencia_e2e");
        t.HasIndex(x => new { x.ContaOrigemId, x.CriadaEm }).IsDescending(false, true).HasDatabaseName("ix_transf_origem_data");
        t.HasIndex(x => new { x.ContaDestinoId, x.CriadaEm }).IsDescending(false, true).HasDatabaseName("ix_transf_destino_data");
        t.HasIndex(x => new { x.ContaOrigemId, x.ConcluidaEm }).HasFilter("status = 'CONCLUIDA' AND tipo IN ('PIX', 'TED')").HasDatabaseName("ix_transf_limite");
        t.HasIndex(x => x.CriadaEm).HasFilter("status = 'PENDENTE_BIOMETRIA'").HasDatabaseName("ix_transf_pendentes");
    }
}

internal sealed class LancamentoConfiguration : IEntityTypeConfiguration<Lancamento>
{
    private static readonly ValueConverter<TipoLancamento, string> DebitoOuCredito = new(
        v => v == TipoLancamento.Debito ? "D" : "C",
        v => v == "D" ? TipoLancamento.Debito : TipoLancamento.Credito);

    public void Configure(EntityTypeBuilder<Lancamento> l)
    {
        l.ToTable("lancamentos", c =>
        {
            c.HasCheckConstraint("ck_lancamentos_tipo", "tipo IN ('D', 'C')");
            c.HasCheckConstraint("ck_lancamentos_valor", "valor_centavos > 0");
        });

        l.HasKey(x => x.Id);
        l.Property(x => x.Id).UseIdentityAlwaysColumn();
        l.Property(x => x.Tipo).HasConversion(DebitoOuCredito).HasColumnType("char(1)").IsRequired();

        l.HasOne<Transferencia>().WithMany().HasForeignKey(x => x.TransferenciaId).OnDelete(DeleteBehavior.Restrict);
        l.HasOne<Conta>().WithMany().HasForeignKey(x => x.ContaId).OnDelete(DeleteBehavior.Restrict);

        // No máximo um débito e um crédito por conta por transferência.
        l.HasIndex(x => new { x.TransferenciaId, x.ContaId, x.Tipo }).IsUnique().HasDatabaseName("uq_lancamento_perna");
        l.HasIndex(x => new { x.ContaId, x.CriadoEm, x.Id }).IsDescending(false, true, true).HasDatabaseName("ix_lanc_extrato");
    }
}
