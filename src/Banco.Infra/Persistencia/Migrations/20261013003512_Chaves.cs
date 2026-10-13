using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Banco.Infra.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class Chaves : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chaves_transferencia",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conta_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    valor = table.Column<string>(type: "text", nullable: false),
                    criada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chaves_transferencia", x => x.id);
                    table.CheckConstraint("ck_chave_formato", "(tipo = 'CPF' AND valor ~ '^[0-9]{11}$') OR\n(tipo = 'EMAIL' AND valor = lower(valor) AND valor ~ '^[^@\\s]+@[^@\\s]+$') OR\n(tipo = 'CELULAR' AND valor ~ '^\\+55[0-9]{10,11}$') OR\n(tipo = 'ALEATORIA' AND valor ~ '^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$')");
                    table.CheckConstraint("ck_chave_tipo", "tipo IN ('CPF', 'EMAIL', 'CELULAR', 'ALEATORIA')");
                    table.ForeignKey(
                        name: "fk_chaves_transferencia_contas_conta_id",
                        column: x => x.conta_id,
                        principalTable: "contas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Quem já tinha conta antes desta migration ganha a chave CPF, como todo cadastro novo.
            migrationBuilder.Sql("""
                INSERT INTO chaves_transferencia (id, conta_id, tipo, valor, criada_em)
                SELECT gen_random_uuid(), c.id, 'CPF', u.cpf, now()
                FROM contas c JOIN usuarios u ON u.id = c.usuario_id
                WHERE c.tipo = 'CLIENTE'
                ON CONFLICT DO NOTHING
                """);

            migrationBuilder.CreateIndex(
                name: "ix_chaves_conta",
                table: "chaves_transferencia",
                column: "conta_id");

            migrationBuilder.CreateIndex(
                name: "uq_chave_valor",
                table: "chaves_transferencia",
                column: "valor",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chaves_transferencia");
        }
    }
}
