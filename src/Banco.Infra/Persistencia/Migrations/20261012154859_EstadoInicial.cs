using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Banco.Infra.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class EstadoInicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "usuarios",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nome = table.Column<string>(type: "text", nullable: false),
                    cpf = table.Column<string>(type: "char(11)", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    celular = table.Column<string>(type: "text", nullable: true),
                    senha_hash = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    cadastro_concluido_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    consentimento_biometrico_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    versao_termo_biometria = table.Column<string>(type: "text", nullable: true),
                    falhas_consecutivas = table.Column<short>(type: "smallint", nullable: false),
                    bloqueado_ate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    atualizado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usuarios", x => x.id);
                    table.CheckConstraint("ck_usuarios_cadastro_concluido", "(status = 'CADASTRO_PENDENTE') = (cadastro_concluido_em IS NULL)");
                    table.CheckConstraint("ck_usuarios_celular", "celular IS NULL OR celular ~ '^\\+55[0-9]{10,11}$'");
                    table.CheckConstraint("ck_usuarios_consentimento_par", "(consentimento_biometrico_em IS NULL) = (versao_termo_biometria IS NULL)");
                    table.CheckConstraint("ck_usuarios_cpf", "cpf ~ '^[0-9]{11}$'");
                    table.CheckConstraint("ck_usuarios_email", "email = lower(email) AND email ~ '^[^@\\s]+@[^@\\s]+$'");
                    table.CheckConstraint("ck_usuarios_falhas", "falhas_consecutivas >= 0");
                    table.CheckConstraint("ck_usuarios_nome", "length(btrim(nome)) BETWEEN 3 AND 120");
                    table.CheckConstraint("ck_usuarios_status", "status IN ('CADASTRO_PENDENTE', 'ATIVO', 'BLOQUEADO', 'ENCERRADO')");
                });

            migrationBuilder.CreateTable(
                name: "contas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    agencia = table.Column<string>(type: "char(4)", nullable: false),
                    numero = table.Column<string>(type: "text", nullable: false),
                    saldo_centavos = table.Column<long>(type: "bigint", nullable: false),
                    limite_diurno_centavos = table.Column<long>(type: "bigint", nullable: false),
                    limite_noturno_centavos = table.Column<long>(type: "bigint", nullable: false),
                    inicio_noturno = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    fim_noturno = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contas", x => x.id);
                    table.CheckConstraint("ck_contas_dono", "(tipo = 'CLIENTE') = (usuario_id IS NOT NULL)");
                    table.CheckConstraint("ck_contas_inicio_noturno", "inicio_noturno IN ('20:00', '22:00')");
                    table.CheckConstraint("ck_contas_limite_diurno", "limite_diurno_centavos >= 0");
                    table.CheckConstraint("ck_contas_limite_noturno", "limite_noturno_centavos >= 0");
                    table.CheckConstraint("ck_contas_limite_noturno_menor", "limite_noturno_centavos <= limite_diurno_centavos");
                    table.CheckConstraint("ck_contas_saldo_nao_negativo", "tipo = 'SISTEMA' OR saldo_centavos >= 0");
                    table.CheckConstraint("ck_contas_status", "status IN ('ATIVA', 'BLOQUEADA', 'ENCERRADA')");
                    table.CheckConstraint("ck_contas_tipo", "tipo IN ('CLIENTE', 'SISTEMA')");
                    table.ForeignKey(
                        name: "fk_contas_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "uq_conta_cliente_por_usuario",
                table: "contas",
                column: "usuario_id",
                unique: true,
                filter: "tipo = 'CLIENTE'");

            migrationBuilder.CreateIndex(
                name: "uq_conta_sistema",
                table: "contas",
                column: "tipo",
                unique: true,
                filter: "tipo = 'SISTEMA'");

            migrationBuilder.CreateIndex(
                name: "uq_contas_numero",
                table: "contas",
                columns: new[] { "agencia", "numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_usuarios_cpf",
                table: "usuarios",
                column: "cpf",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_usuarios_email",
                table: "usuarios",
                column: "email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contas");

            migrationBuilder.DropTable(
                name: "usuarios");
        }
    }
}
