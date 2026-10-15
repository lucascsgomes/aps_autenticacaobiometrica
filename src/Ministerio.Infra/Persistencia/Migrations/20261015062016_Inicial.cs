using System;
using System.Net;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Ministerio.Infra.Persistencia.Migrations
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "propriedades_rurais",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    municipio = table.Column<string>(type: "text", nullable: false),
                    uf = table.Column<string>(type: "char(2)", nullable: false),
                    agrotoxico = table.Column<string>(type: "text", nullable: false),
                    ano = table.Column<int>(type: "integer", nullable: false),
                    propriedade = table.Column<string>(type: "text", nullable: false),
                    quantidade_litros = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    area_hectares = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    proprietario = table.Column<string>(type: "text", nullable: false),
                    documento = table.Column<string>(type: "text", nullable: false),
                    multa_centavos = table.Column<long>(type: "bigint", nullable: false),
                    situacao = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_propriedades_rurais", x => x.id);
                    table.CheckConstraint("ck_propriedade_uf", "uf ~ '^[A-Z]{2}$'");
                    table.CheckConstraint("ck_propriedade_valores", "quantidade_litros >= 0 AND area_hectares > 0 AND multa_centavos >= 0");
                });

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
                name: "desafios_biometricos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    finalidade = table.Column<string>(type: "text", nullable: false),
                    nonce = table.Column<byte[]>(type: "bytea", nullable: false),
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
                    table.CheckConstraint("ck_desafio_finalidade", "finalidade IN ('CADASTRO', 'LOGIN', 'ACESSO_NIVEL2', 'ACESSO_NIVEL3', 'TROCAR_SENHA', 'RECADASTRO')");
                    table.CheckConstraint("ck_desafio_hash", "hash_operacao IS NULL OR octet_length(hash_operacao) = 32");
                    table.CheckConstraint("ck_desafio_limiar", "limiar_aplicado BETWEEN 0.300 AND 0.900");
                    table.CheckConstraint("ck_desafio_metodo", "(usado_em IS NULL) = (metodo IS NULL)");
                    table.CheckConstraint("ck_desafio_metodo_valor", "metodo IN ('FACE', 'SENHA')");
                    table.CheckConstraint("ck_desafio_nonce", "octet_length(nonce) = 32");
                    table.CheckConstraint("ck_desafio_uso", "usado_em IS NULL OR usado_em >= criado_em");
                    table.CheckConstraint("ck_desafio_usuario", "usuario_id IS NOT NULL OR finalidade = 'LOGIN'");
                    table.CheckConstraint("ck_desafio_validade", "expira_em > criado_em AND expira_em <= criado_em + interval '5 minutes'");
                    table.ForeignKey(
                        name: "fk_desafios_biometricos_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    familia_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    criado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expira_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    usado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revogado_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    substituido_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                    table.CheckConstraint("ck_refresh_hash", "octet_length(token_hash) = 32");
                    table.ForeignKey(
                        name: "fk_refresh_tokens_refresh_tokens_substituido_por",
                        column: x => x.substituido_por,
                        principalTable: "refresh_tokens",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_refresh_tokens_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "registros_de_acesso",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    ocorrido_em = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    evento = table.Column<string>(type: "text", nullable: false),
                    resultado = table.Column<string>(type: "text", nullable: false),
                    motivo = table.Column<string>(type: "text", nullable: true),
                    ip = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_registros_de_acesso", x => x.id);
                    table.CheckConstraint("ck_acesso_evento", "evento IN ('LOGIN_SENHA', 'LOGIN_ROSTO', 'LOGIN_IDENTIFICACAO', 'CONSULTA_NIVEL1', 'CONSULTA_NIVEL2', 'CONSULTA_NIVEL3', 'RELATORIO')");
                    table.CheckConstraint("ck_acesso_motivo", "(resultado = 'RECUSADO') = (motivo IS NOT NULL)");
                    table.CheckConstraint("ck_acesso_resultado", "resultado IN ('ACEITO', 'RECUSADO')");
                    table.ForeignKey(
                        name: "fk_registros_de_acesso_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

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

            migrationBuilder.InsertData(
                table: "propriedades_rurais",
                columns: new[] { "id", "agrotoxico", "ano", "area_hectares", "documento", "multa_centavos", "municipio", "propriedade", "proprietario", "quantidade_litros", "situacao", "uf" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0000-000000000001"), "Paraquate", 2022, 850.00m, "11.111.111/0001-11", 48000000L, "Rio Verde", "Fazenda Vale Dourado", "Agropecuária Vale Dourado Ltda", 1200.00m, "Autuado", "GO" },
                    { new Guid("00000000-0000-0000-0000-000000000002"), "Carbofurano", 2023, 1320.00m, "22.222.222/0001-22", 92000000L, "Sorriso", "Fazenda Santa Aurora", "Aurora Grãos S.A.", 640.50m, "Em apuração", "MT" },
                    { new Guid("00000000-0000-0000-0000-000000000003"), "Endossulfam", 2021, 64.00m, "33.333.333/0001-33", 6500000L, "Lucas do Rio Verde", "Sítio Três Irmãos", "Irmãos Almeida Agrícola", 85.00m, "Multa paga", "MT" },
                    { new Guid("00000000-0000-0000-0000-000000000004"), "Metamidofós", 2024, 540.00m, "44.444.444/0001-44", 31000000L, "Uberlândia", "Fazenda Boa Esperança", "Boa Esperança Cafés Ltda", 410.00m, "Em recurso", "MG" },
                    { new Guid("00000000-0000-0000-0000-000000000005"), "Paraquate", 2022, 720.00m, "55.555.555/0001-55", 53000000L, "Cascavel", "Fazenda Campo Largo", "Campo Largo Cooperativa", 980.00m, "Processo no MPF", "PR" },
                    { new Guid("00000000-0000-0000-0000-000000000006"), "Clorpirifós", 2023, 410.00m, "66.666.666/0001-66", 15000000L, "Ponta Grossa", "Fazenda Rio Claro", "Rio Claro Agro Ltda", 300.00m, "Autuado", "PR" },
                    { new Guid("00000000-0000-0000-0000-000000000007"), "Carbofurano", 2024, 2200.00m, "77.777.777/0001-77", 125000000L, "Barreiras", "Fazenda Oeste Baiano", "Oeste Baiano Agronegócios", 1500.00m, "Em apuração", "BA" },
                    { new Guid("00000000-0000-0000-0000-000000000008"), "Endossulfam", 2021, 380.00m, "88.888.888/0001-88", 9800000L, "Luís Eduardo Magalhães", "Fazenda Serra Azul", "Serra Azul Produtores", 220.00m, "Multa paga", "BA" },
                    { new Guid("00000000-0000-0000-0000-000000000009"), "Paraquate", 2023, 960.00m, "99.999.999/0001-99", 61000000L, "Dourados", "Fazenda Pantanal Sul", "Pantanal Sul Grãos Ltda", 760.00m, "Em recurso", "MS" },
                    { new Guid("00000000-0000-0000-0000-000000000010"), "Metamidofós", 2022, 48.00m, "10.101.010/0001-10", 4200000L, "Maracaju", "Sítio Boa Vista", "Família Pereira Agrícola", 55.00m, "Multa paga", "MS" },
                    { new Guid("00000000-0000-0000-0000-000000000011"), "Clorpirifós", 2024, 680.00m, "12.121.212/0001-12", 27500000L, "Passo Fundo", "Fazenda Coxilha Verde", "Coxilha Verde Ltda", 520.00m, "Autuado", "RS" },
                    { new Guid("00000000-0000-0000-0000-000000000012"), "Carbofurano", 2021, 190.00m, "13.131.313/0001-13", 8800000L, "Santa Rosa", "Granja Santa Rosa", "Granja Santa Rosa S.A.", 140.00m, "Multa paga", "RS" },
                    { new Guid("00000000-0000-0000-0000-000000000013"), "Paraquate", 2024, 1850.00m, "14.141.414/0001-14", 89000000L, "Balsas", "Fazenda Chapada Alta", "Chapada Alta Agro", 1100.00m, "Em apuração", "MA" },
                    { new Guid("00000000-0000-0000-0000-000000000014"), "Endossulfam", 2023, 600.00m, "15.151.515/0001-15", 19000000L, "Uruçuí", "Fazenda Cerrado Norte", "Cerrado Norte Ltda", 330.00m, "Processo no MPF", "PI" },
                    { new Guid("00000000-0000-0000-0000-000000000015"), "Metamidofós", 2022, 22.00m, "16.161.616/0001-16", 1800000L, "Luziânia", "Chácara Recanto", "Recanto Hortifruti", 30.00m, "Multa paga", "GO" },
                    { new Guid("00000000-0000-0000-0000-000000000016"), "Clorpirifós", 2024, 1430.00m, "17.171.717/0001-17", 54000000L, "Primavera do Leste", "Fazenda Três Marias", "Três Marias Agropecuária", 870.00m, "Em apuração", "MT" },
                    { new Guid("00000000-0000-0000-0000-000000000017"), "Carbofurano", 2022, 58.00m, "18.181.818/0001-18", 5200000L, "Londrina", "Sítio Água Limpa", "Cafeicultores Água Limpa", 70.00m, "Em recurso", "PR" },
                    { new Guid("00000000-0000-0000-0000-000000000018"), "Paraquate", 2023, 1280.00m, "19.191.919/0001-19", 43000000L, "Araguaína", "Fazenda Rio Tocantins", "Rio Tocantins Pecuária", 690.00m, "Autuado", "TO" },
                    { new Guid("00000000-0000-0000-0000-000000000019"), "Endossulfam", 2021, 150.00m, "20.202.020/0001-20", 6100000L, "Palmas", "Fazenda Jalapão", "Jalapão Agro Ltda", 95.00m, "Multa paga", "TO" },
                    { new Guid("00000000-0000-0000-0000-000000000020"), "Metamidofós", 2024, 330.00m, "21.212.121/0001-21", 20500000L, "Petrolina", "Fazenda Vale do Sol", "Vale do Sol Fruticultura", 260.00m, "Em apuração", "PE" }
                });

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
                name: "ix_propriedades_local",
                table: "propriedades_rurais",
                columns: new[] { "uf", "municipio" });

            migrationBuilder.CreateIndex(
                name: "ix_refresh_familia",
                table: "refresh_tokens",
                column: "familia_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_substituido_por",
                table: "refresh_tokens",
                column: "substituido_por");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_usuario",
                table: "refresh_tokens",
                column: "usuario_id",
                filter: "revogado_em IS NULL");

            migrationBuilder.CreateIndex(
                name: "uq_refresh_hash",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_acesso_quando",
                table: "registros_de_acesso",
                column: "ocorrido_em",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_registros_de_acesso_usuario_id",
                table: "registros_de_acesso",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "uq_template_usuario",
                table: "templates_faciais",
                column: "usuario_id",
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
                name: "desafios_biometricos");

            migrationBuilder.DropTable(
                name: "propriedades_rurais");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "registros_de_acesso");

            migrationBuilder.DropTable(
                name: "templates_faciais");

            migrationBuilder.DropTable(
                name: "usuarios");
        }
    }
}
