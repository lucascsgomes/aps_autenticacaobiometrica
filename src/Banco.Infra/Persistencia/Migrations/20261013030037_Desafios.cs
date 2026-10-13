using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Banco.Infra.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class Desafios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "desafios_biometricos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    finalidade = table.Column<string>(type: "text", nullable: false),
                    nonce = table.Column<byte[]>(type: "bytea", nullable: false),
                    transferencia_id = table.Column<Guid>(type: "uuid", nullable: true),
                    valor_centavos = table.Column<long>(type: "bigint", nullable: true),
                    conta_destino_id = table.Column<Guid>(type: "uuid", nullable: true),
                    hash_operacao = table.Column<byte[]>(type: "bytea", nullable: true),
                    limiar_aplicado = table.Column<decimal>(type: "numeric(4,3)", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expira_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    usado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    metodo = table.Column<string>(type: "text", nullable: true),
                    devolvido_vezes = table.Column<short>(type: "smallint", nullable: false),
                    ip = table.Column<IPAddress>(type: "inet", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_desafios_biometricos", x => x.id);
                    table.CheckConstraint("ck_desafio_amarrado", "finalidade IN ('LOGIN', 'CADASTRO') OR hash_operacao IS NOT NULL");
                    table.CheckConstraint("ck_desafio_devolvido", "devolvido_vezes BETWEEN 0 AND 5");
                    table.CheckConstraint("ck_desafio_finalidade", "finalidade IN ('CADASTRO', 'LOGIN', 'TRANSFERENCIA', 'ALTERAR_LIMITE', 'NOVA_CHAVE', 'NOVO_FAVORECIDO', 'TROCAR_SENHA', 'RECADASTRO')");
                    table.CheckConstraint("ck_desafio_hash", "hash_operacao IS NULL OR octet_length(hash_operacao) = 32");
                    table.CheckConstraint("ck_desafio_limiar", "limiar_aplicado BETWEEN 0.300 AND 0.900");
                    table.CheckConstraint("ck_desafio_metodo", "(usado_em IS NULL) = (metodo IS NULL)");
                    table.CheckConstraint("ck_desafio_metodo_valor", "metodo IN ('FACE', 'SENHA')");
                    table.CheckConstraint("ck_desafio_nonce", "octet_length(nonce) = 32");
                    table.CheckConstraint("ck_desafio_transferencia", "finalidade <> 'TRANSFERENCIA' OR (transferencia_id IS NOT NULL AND valor_centavos IS NOT NULL AND conta_destino_id IS NOT NULL AND hash_operacao IS NOT NULL)");
                    table.CheckConstraint("ck_desafio_uso", "usado_em IS NULL OR usado_em >= criado_em");
                    table.CheckConstraint("ck_desafio_usuario", "usuario_id IS NOT NULL OR finalidade = 'LOGIN'");
                    table.CheckConstraint("ck_desafio_validade", "expira_em > criado_em AND expira_em <= criado_em + interval '5 minutes'");
                    table.CheckConstraint("ck_desafio_valor", "valor_centavos IS NULL OR valor_centavos > 0");
                    table.ForeignKey(
                        name: "fk_desafios_biometricos_contas_conta_destino_id",
                        column: x => x.conta_destino_id,
                        principalTable: "contas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_desafios_biometricos_transferencias_transferencia_id",
                        column: x => x.transferencia_id,
                        principalTable: "transferencias",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_desafios_biometricos_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_desafios_biometricos_conta_destino_id",
                table: "desafios_biometricos",
                column: "conta_destino_id");

            migrationBuilder.CreateIndex(
                name: "ix_desafios_expiracao",
                table: "desafios_biometricos",
                column: "expira_em",
                filter: "usado_em IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_desafios_usuario",
                table: "desafios_biometricos",
                columns: new[] { "usuario_id", "criado_em" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "uq_desafio_nonce",
                table: "desafios_biometricos",
                column: "nonce",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_desafio_por_transferencia",
                table: "desafios_biometricos",
                column: "transferencia_id",
                unique: true,
                filter: "transferencia_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "desafios_biometricos");
        }
    }
}
