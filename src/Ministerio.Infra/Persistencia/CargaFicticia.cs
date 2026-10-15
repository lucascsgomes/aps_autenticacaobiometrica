using Ministerio.Dominio.Acesso;

namespace Ministerio.Infra.Persistencia;

/// <summary>
/// Dados 100% inventados para a demonstração: nomes, documentos, locais e valores não existem. Os produtos são agrotóxicos
/// realmente proibidos ou banidos no Brasil, só para o cenário fazer sentido. Entra na base pela migration.
/// </summary>
internal static class CargaFicticia
{
    // (município, UF, agrotóxico, ano, propriedade, litros, hectares, proprietário, documento, multa em reais, situação)
    private static readonly (string Municipio, string Uf, string Produto, int Ano, string Nome, decimal Litros, decimal Hectares, string Dono, string Doc, long Multa, string Situacao)[] Linhas =
    [
        ("Rio Verde", "GO", "Paraquate", 2022, "Fazenda Vale Dourado", 1200.00m, 850.00m, "Agropecuária Vale Dourado Ltda", "11.111.111/0001-11", 480_000, "Autuado"),
        ("Sorriso", "MT", "Carbofurano", 2023, "Fazenda Santa Aurora", 640.50m, 1320.00m, "Aurora Grãos S.A.", "22.222.222/0001-22", 920_000, "Em apuração"),
        ("Lucas do Rio Verde", "MT", "Endossulfam", 2021, "Sítio Três Irmãos", 85.00m, 64.00m, "Irmãos Almeida Agrícola", "33.333.333/0001-33", 65_000, "Multa paga"),
        ("Uberlândia", "MG", "Metamidofós", 2024, "Fazenda Boa Esperança", 410.00m, 540.00m, "Boa Esperança Cafés Ltda", "44.444.444/0001-44", 310_000, "Em recurso"),
        ("Cascavel", "PR", "Paraquate", 2022, "Fazenda Campo Largo", 980.00m, 720.00m, "Campo Largo Cooperativa", "55.555.555/0001-55", 530_000, "Processo no MPF"),
        ("Ponta Grossa", "PR", "Clorpirifós", 2023, "Fazenda Rio Claro", 300.00m, 410.00m, "Rio Claro Agro Ltda", "66.666.666/0001-66", 150_000, "Autuado"),
        ("Barreiras", "BA", "Carbofurano", 2024, "Fazenda Oeste Baiano", 1500.00m, 2200.00m, "Oeste Baiano Agronegócios", "77.777.777/0001-77", 1_250_000, "Em apuração"),
        ("Luís Eduardo Magalhães", "BA", "Endossulfam", 2021, "Fazenda Serra Azul", 220.00m, 380.00m, "Serra Azul Produtores", "88.888.888/0001-88", 98_000, "Multa paga"),
        ("Dourados", "MS", "Paraquate", 2023, "Fazenda Pantanal Sul", 760.00m, 960.00m, "Pantanal Sul Grãos Ltda", "99.999.999/0001-99", 610_000, "Em recurso"),
        ("Maracaju", "MS", "Metamidofós", 2022, "Sítio Boa Vista", 55.00m, 48.00m, "Família Pereira Agrícola", "10.101.010/0001-10", 42_000, "Multa paga"),
        ("Passo Fundo", "RS", "Clorpirifós", 2024, "Fazenda Coxilha Verde", 520.00m, 680.00m, "Coxilha Verde Ltda", "12.121.212/0001-12", 275_000, "Autuado"),
        ("Santa Rosa", "RS", "Carbofurano", 2021, "Granja Santa Rosa", 140.00m, 190.00m, "Granja Santa Rosa S.A.", "13.131.313/0001-13", 88_000, "Multa paga"),
        ("Balsas", "MA", "Paraquate", 2024, "Fazenda Chapada Alta", 1100.00m, 1850.00m, "Chapada Alta Agro", "14.141.414/0001-14", 890_000, "Em apuração"),
        ("Uruçuí", "PI", "Endossulfam", 2023, "Fazenda Cerrado Norte", 330.00m, 600.00m, "Cerrado Norte Ltda", "15.151.515/0001-15", 190_000, "Processo no MPF"),
        ("Luziânia", "GO", "Metamidofós", 2022, "Chácara Recanto", 30.00m, 22.00m, "Recanto Hortifruti", "16.161.616/0001-16", 18_000, "Multa paga"),
        ("Primavera do Leste", "MT", "Clorpirifós", 2024, "Fazenda Três Marias", 870.00m, 1430.00m, "Três Marias Agropecuária", "17.171.717/0001-17", 540_000, "Em apuração"),
        ("Londrina", "PR", "Carbofurano", 2022, "Sítio Água Limpa", 70.00m, 58.00m, "Cafeicultores Água Limpa", "18.181.818/0001-18", 52_000, "Em recurso"),
        ("Araguaína", "TO", "Paraquate", 2023, "Fazenda Rio Tocantins", 690.00m, 1280.00m, "Rio Tocantins Pecuária", "19.191.919/0001-19", 430_000, "Autuado"),
        ("Palmas", "TO", "Endossulfam", 2021, "Fazenda Jalapão", 95.00m, 150.00m, "Jalapão Agro Ltda", "20.202.020/0001-20", 61_000, "Multa paga"),
        ("Petrolina", "PE", "Metamidofós", 2024, "Fazenda Vale do Sol", 260.00m, 330.00m, "Vale do Sol Fruticultura", "21.212.121/0001-21", 205_000, "Em apuração"),
    ];

    public static PropriedadeRural[] Propriedades() =>
        Linhas.Select((l, i) => new PropriedadeRural
        {
            // Id fixo: a mesma carga gera sempre as mesmas linhas, e a migration não muda a cada geração.
            Id = new Guid($"00000000-0000-0000-0000-{i + 1:D12}"),
            Municipio = l.Municipio,
            Uf = l.Uf,
            Agrotoxico = l.Produto,
            Ano = l.Ano,
            Propriedade = l.Nome,
            QuantidadeLitros = l.Litros,
            AreaHectares = l.Hectares,
            Proprietario = l.Dono,
            Documento = l.Doc,
            MultaCentavos = l.Multa * 100,
            Situacao = l.Situacao,
        }).ToArray();
}
