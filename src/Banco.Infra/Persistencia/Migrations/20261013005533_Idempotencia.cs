using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Banco.Infra.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class Idempotencia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "idempotency_keys",
                columns: table => new
                {
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chave = table.Column<string>(type: "text", nullable: false),
                    escopo = table.Column<string>(type: "text", nullable: false),
                    request_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    estado = table.Column<string>(type: "text", nullable: false),
                    status_code = table.Column<short>(type: "smallint", nullable: true),
                    response_body = table.Column<string>(type: "jsonb", nullable: true),
                    criada_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expira_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_idempotency_keys", x => new { x.usuario_id, x.chave });
                    table.CheckConstraint("ck_idem_chave", "length(chave) BETWEEN 8 AND 255");
                    table.CheckConstraint("ck_idem_estado", "estado IN ('PROCESSANDO', 'CONCLUIDA')");
                    table.CheckConstraint("ck_idem_hash", "octet_length(request_hash) = 32");
                    table.CheckConstraint("ck_idem_resposta", "(estado = 'CONCLUIDA') = (status_code IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_idempotency_keys_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_idem_expiracao",
                table: "idempotency_keys",
                column: "expira_em");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "idempotency_keys");
        }
    }
}
