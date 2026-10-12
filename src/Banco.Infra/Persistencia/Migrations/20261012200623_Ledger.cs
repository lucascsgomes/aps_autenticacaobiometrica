using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Banco.Infra.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class Ledger : Migration
    {
        // Regras que o EF não modela: imutabilidade do ledger, transições de estado e partida dobrada no COMMIT.
        private const string RegrasDoBancoSql = """
            CREATE FUNCTION trg_somente_insercao() RETURNS trigger
            LANGUAGE plpgsql AS $$
            BEGIN
                RAISE EXCEPTION 'Tabela % é somente inserção (operação %).', TG_TABLE_NAME, TG_OP
                    USING ERRCODE = 'integrity_constraint_violation';
            END $$;

            CREATE TABLE transferencia_historico (
                id               bigint      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                transferencia_id uuid        NOT NULL REFERENCES transferencias (id),
                de_status        text,
                para_status      text        NOT NULL,
                motivo           text,
                em               timestamptz NOT NULL DEFAULT now()
            );
            CREATE INDEX ix_hist_transf ON transferencia_historico (transferencia_id, id);
            CREATE TRIGGER trg_hist_imutavel BEFORE UPDATE OR DELETE ON transferencia_historico
                FOR EACH ROW EXECUTE FUNCTION trg_somente_insercao();

            CREATE FUNCTION trg_transferencia_transicao() RETURNS trigger
            LANGUAGE plpgsql AS $$
            BEGIN
                IF TG_OP = 'INSERT' THEN
                    INSERT INTO transferencia_historico (transferencia_id, de_status, para_status)
                    VALUES (NEW.id, NULL, NEW.status);
                    RETURN NEW;
                END IF;

                IF NEW.status = OLD.status THEN
                    RETURN NEW;
                END IF;

                IF NOT (
                    (OLD.status = 'PENDENTE_BIOMETRIA' AND NEW.status IN ('PROCESSANDO', 'FALHA', 'EXPIRADA')) OR
                    (OLD.status = 'PROCESSANDO'        AND NEW.status IN ('CONCLUIDA', 'FALHA'))
                ) THEN
                    RAISE EXCEPTION 'Transição inválida de % para %', OLD.status, NEW.status
                        USING ERRCODE = 'check_violation';
                END IF;

                INSERT INTO transferencia_historico (transferencia_id, de_status, para_status, motivo)
                VALUES (NEW.id, OLD.status, NEW.status, NEW.motivo_falha);
                RETURN NEW;
            END $$;

            CREATE TRIGGER trg_transferencia_transicao
                AFTER INSERT OR UPDATE OF status ON transferencias
                FOR EACH ROW EXECUTE FUNCTION trg_transferencia_transicao();

            CREATE TRIGGER trg_lancamentos_imutavel BEFORE UPDATE OR DELETE ON lancamentos
                FOR EACH ROW EXECUTE FUNCTION trg_somente_insercao();
            CREATE TRIGGER trg_lancamentos_sem_truncate BEFORE TRUNCATE ON lancamentos
                FOR EACH STATEMENT EXECUTE FUNCTION trg_somente_insercao();

            CREATE FUNCTION trg_ledger_balanceado() RETURNS trigger
            LANGUAGE plpgsql AS $$
            DECLARE
                debitos  bigint;
                creditos bigint;
            BEGIN
                SELECT COALESCE(SUM(valor_centavos) FILTER (WHERE tipo = 'D'), 0),
                       COALESCE(SUM(valor_centavos) FILTER (WHERE tipo = 'C'), 0)
                  INTO debitos, creditos
                  FROM lancamentos
                 WHERE transferencia_id = NEW.transferencia_id;

                IF debitos <> creditos THEN
                    RAISE EXCEPTION 'Ledger desbalanceado na transferência %: débitos %, créditos %',
                        NEW.transferencia_id, debitos, creditos
                        USING ERRCODE = 'integrity_constraint_violation';
                END IF;
                RETURN NULL;
            END $$;

            CREATE CONSTRAINT TRIGGER trg_ledger_balanceado
                AFTER INSERT ON lancamentos
                DEFERRABLE INITIALLY DEFERRED
                FOR EACH ROW EXECUTE FUNCTION trg_ledger_balanceado();
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "transferencias",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "text", nullable: false, defaultValue: "PIX"),
                    conta_origem_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conta_destino_id = table.Column<Guid>(type: "uuid", nullable: false),
                    valor_centavos = table.Column<long>(type: "bigint", nullable: false),
                    descricao = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    motivo_falha = table.Column<string>(type: "text", nullable: true),
                    e2e_id = table.Column<string>(type: "text", nullable: true),
                    metodo_confirmacao = table.Column<string>(type: "text", nullable: true),
                    criada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    concluida_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transferencias", x => x.id);
                    table.CheckConstraint("ck_conclusao", "(status = 'CONCLUIDA') = (concluida_em IS NOT NULL)");
                    table.CheckConstraint("ck_contas_diferentes", "conta_origem_id <> conta_destino_id");
                    table.CheckConstraint("ck_falha_tem_motivo", "(status = 'FALHA') = (motivo_falha IS NOT NULL)");
                    table.CheckConstraint("ck_transferencias_descricao", "length(descricao) <= 140");
                    table.CheckConstraint("ck_transferencias_e2e", "e2e_id ~ '^E[0-9]{8}[0-9]{12}[A-Za-z0-9]{11}$'");
                    table.CheckConstraint("ck_transferencias_metodo", "metodo_confirmacao IN ('FACE', 'SENHA')");
                    table.CheckConstraint("ck_transferencias_status", "status IN ('PENDENTE_BIOMETRIA', 'PROCESSANDO', 'CONCLUIDA', 'FALHA', 'EXPIRADA')");
                    table.CheckConstraint("ck_transferencias_tipo", "tipo IN ('PIX', 'TED', 'CREDITO_INICIAL')");
                    table.CheckConstraint("ck_transferencias_valor", "valor_centavos > 0");
                    table.ForeignKey(
                        name: "fk_transferencias_contas_conta_destino_id",
                        column: x => x.conta_destino_id,
                        principalTable: "contas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_transferencias_contas_conta_origem_id",
                        column: x => x.conta_origem_id,
                        principalTable: "contas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "lancamentos",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    transferencia_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "char(1)", nullable: false),
                    valor_centavos = table.Column<long>(type: "bigint", nullable: false),
                    saldo_apos_centavos = table.Column<long>(type: "bigint", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lancamentos", x => x.id);
                    table.CheckConstraint("ck_lancamentos_tipo", "tipo IN ('D', 'C')");
                    table.CheckConstraint("ck_lancamentos_valor", "valor_centavos > 0");
                    table.ForeignKey(
                        name: "fk_lancamentos_contas_conta_id",
                        column: x => x.conta_id,
                        principalTable: "contas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_lancamentos_transferencias_transferencia_id",
                        column: x => x.transferencia_id,
                        principalTable: "transferencias",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_lanc_extrato",
                table: "lancamentos",
                columns: new[] { "conta_id", "criado_em", "id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "uq_lancamento_perna",
                table: "lancamentos",
                columns: new[] { "transferencia_id", "conta_id", "tipo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_transf_destino_data",
                table: "transferencias",
                columns: new[] { "conta_destino_id", "criada_em" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_transf_limite",
                table: "transferencias",
                columns: new[] { "conta_origem_id", "concluida_em" },
                filter: "status = 'CONCLUIDA' AND tipo IN ('PIX', 'TED')");

            migrationBuilder.CreateIndex(
                name: "ix_transf_origem_data",
                table: "transferencias",
                columns: new[] { "conta_origem_id", "criada_em" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_transf_pendentes",
                table: "transferencias",
                column: "criada_em",
                filter: "status = 'PENDENTE_BIOMETRIA'");

            migrationBuilder.CreateIndex(
                name: "uq_transferencia_e2e",
                table: "transferencias",
                column: "e2e_id",
                unique: true);

            migrationBuilder.Sql(RegrasDoBancoSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TABLE transferencia_historico;
                DROP FUNCTION trg_transferencia_transicao();
                DROP FUNCTION trg_ledger_balanceado();
                DROP FUNCTION trg_somente_insercao();
                """);

            migrationBuilder.DropTable(
                name: "lancamentos");

            migrationBuilder.DropTable(
                name: "transferencias");
        }
    }
}
