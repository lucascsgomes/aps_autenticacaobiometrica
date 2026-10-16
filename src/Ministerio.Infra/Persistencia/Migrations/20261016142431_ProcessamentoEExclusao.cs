using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ministerio.Infra.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class ProcessamentoEExclusao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_acesso_evento",
                table: "registros_de_acesso");

            migrationBuilder.DropCheckConstraint(
                name: "ck_desafio_finalidade",
                table: "desafios_biometricos");

            migrationBuilder.CreateTable(
                name: "configuracoes",
                columns: table => new
                {
                    chave = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    valor = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_configuracoes", x => x.chave);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_acesso_evento",
                table: "registros_de_acesso",
                sql: "evento IN ('LOGIN_SENHA', 'LOGIN_ROSTO', 'LOGIN_IDENTIFICACAO', 'CONSULTA_NIVEL1', 'CONSULTA_NIVEL2', 'CONSULTA_NIVEL3', 'RELATORIO', 'ALTERACAO_DE_ACESSO', 'ALTERACAO_DE_PROCESSAMENTO', 'EXCLUSAO_DE_CADASTRO')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_desafio_finalidade",
                table: "desafios_biometricos",
                sql: "finalidade IN ('CADASTRO', 'LOGIN', 'ACESSO_NIVEL2', 'ACESSO_NIVEL3', 'TROCAR_SENHA', 'RECADASTRO', 'ALTERAR_ACESSO', 'ALTERAR_PROCESSAMENTO', 'EXCLUIR_CADASTRO')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "configuracoes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_acesso_evento",
                table: "registros_de_acesso");

            migrationBuilder.DropCheckConstraint(
                name: "ck_desafio_finalidade",
                table: "desafios_biometricos");

            migrationBuilder.AddCheckConstraint(
                name: "ck_acesso_evento",
                table: "registros_de_acesso",
                sql: "evento IN ('LOGIN_SENHA', 'LOGIN_ROSTO', 'LOGIN_IDENTIFICACAO', 'CONSULTA_NIVEL1', 'CONSULTA_NIVEL2', 'CONSULTA_NIVEL3', 'RELATORIO', 'ALTERACAO_DE_ACESSO')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_desafio_finalidade",
                table: "desafios_biometricos",
                sql: "finalidade IN ('CADASTRO', 'LOGIN', 'ACESSO_NIVEL2', 'ACESSO_NIVEL3', 'TROCAR_SENHA', 'RECADASTRO', 'ALTERAR_ACESSO')");
        }
    }
}
