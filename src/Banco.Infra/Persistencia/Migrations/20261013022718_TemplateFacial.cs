using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Banco.Infra.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class TemplateFacial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "templates_faciais",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    embedding_cifrado = table.Column<byte[]>(type: "bytea", nullable: false),
                    nonce = table.Column<byte[]>(type: "bytea", nullable: false),
                    chave_versao = table.Column<short>(type: "smallint", nullable: false),
                    modelo = table.Column<string>(type: "text", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_templates_faciais", x => x.id);
                    table.CheckConstraint("ck_template_chave_versao", "chave_versao > 0");
                    table.CheckConstraint("ck_template_nonce", "octet_length(nonce) = 12");
                    table.CheckConstraint("ck_template_tamanho", "octet_length(embedding_cifrado) = 528");
                    table.ForeignKey(
                        name: "fk_templates_faciais_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "uq_template_usuario",
                table: "templates_faciais",
                column: "usuario_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "templates_faciais");
        }
    }
}
