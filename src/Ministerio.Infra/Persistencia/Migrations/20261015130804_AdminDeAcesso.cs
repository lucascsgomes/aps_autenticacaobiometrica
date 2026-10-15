using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ministerio.Infra.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class AdminDeAcesso : Migration
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

            migrationBuilder.AddColumn<bool>(
                name: "admin",
                table: "usuarios",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<short>(
                name: "nivel",
                table: "usuarios",
                type: "smallint",
                nullable: false,
                defaultValue: (short)1);

            migrationBuilder.AddColumn<string>(
                name: "detalhe",
                table: "registros_de_acesso",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_usuarios_nivel",
                table: "usuarios",
                sql: "nivel BETWEEN 1 AND 3");

            migrationBuilder.AddCheckConstraint(
                name: "ck_acesso_evento",
                table: "registros_de_acesso",
                sql: "evento IN ('LOGIN_SENHA', 'LOGIN_ROSTO', 'LOGIN_IDENTIFICACAO', 'CONSULTA_NIVEL1', 'CONSULTA_NIVEL2', 'CONSULTA_NIVEL3', 'RELATORIO', 'ALTERACAO_DE_ACESSO')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_desafio_finalidade",
                table: "desafios_biometricos",
                sql: "finalidade IN ('CADASTRO', 'LOGIN', 'ACESSO_NIVEL2', 'ACESSO_NIVEL3', 'TROCAR_SENHA', 'RECADASTRO', 'ALTERAR_ACESSO')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_usuarios_nivel",
                table: "usuarios");

            migrationBuilder.DropCheckConstraint(
                name: "ck_acesso_evento",
                table: "registros_de_acesso");

            migrationBuilder.DropCheckConstraint(
                name: "ck_desafio_finalidade",
                table: "desafios_biometricos");

            migrationBuilder.DropColumn(
                name: "admin",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "nivel",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "detalhe",
                table: "registros_de_acesso");

            migrationBuilder.AddCheckConstraint(
                name: "ck_acesso_evento",
                table: "registros_de_acesso",
                sql: "evento IN ('LOGIN_SENHA', 'LOGIN_ROSTO', 'LOGIN_IDENTIFICACAO', 'CONSULTA_NIVEL1', 'CONSULTA_NIVEL2', 'CONSULTA_NIVEL3', 'RELATORIO')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_desafio_finalidade",
                table: "desafios_biometricos",
                sql: "finalidade IN ('CADASTRO', 'LOGIN', 'ACESSO_NIVEL2', 'ACESSO_NIVEL3', 'TROCAR_SENHA', 'RECADASTRO')");
        }
    }
}
