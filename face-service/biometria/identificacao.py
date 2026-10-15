from dataclasses import dataclass
from biometria.comparacao import THRESHOLD_COSINE, comparar, corresponde

# Folga mínima entre o melhor e o segundo melhor candidato. Comparar com muita gente aumenta a chance de confundir duas
# pessoas parecidas; se a diferença é pequena, o sistema prefere recusar a escolher. Valor inicial, ainda sem calibração.
MARGEM_PADRAO = 0.05


@dataclass(frozen=True)
class ResultadoIdentificacao:
    identificado: bool
    candidato: str | None  # só preenchido quando identificado
    score: float  # melhor score, mesmo quando recusado (uso interno e relatórios)
    segundo_score: float | None
    ambiguo: bool  # recusado porque o segundo candidato ficou perto demais do primeiro


def identificar(modelo_sface, candidatos, caracteristicas, limiar=THRESHOLD_COSINE, margem=MARGEM_PADRAO):
    """1 para N: compara a amostra com todos os candidatos `(id, vetor)` e decide quem é, se alguém for."""
    if not candidatos:
        raise ValueError('Informe ao menos um candidato.')
    pontuacoes = sorted(((comparar(modelo_sface, vetor, caracteristicas), id_) for id_, vetor in candidatos),
                        key=lambda par: (-par[0], par[1]))
    melhor, melhor_id = pontuacoes[0]
    segundo = pontuacoes[1][0] if len(pontuacoes) > 1 else None
    if not corresponde(melhor, limiar):
        return ResultadoIdentificacao(False, None, melhor, segundo, False)
    if segundo is not None and melhor - segundo < margem:
        return ResultadoIdentificacao(False, None, melhor, segundo, True)
    return ResultadoIdentificacao(True, melhor_id, melhor, segundo, False)
