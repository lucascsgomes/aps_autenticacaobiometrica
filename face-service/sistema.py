from dataclasses import dataclass
from pathlib import Path
from banco.memoria import BancoMemoria, validar_identificador
from biometria.pipeline import PipelineFacial
from biometria.comparacao import comparar, corresponde


@dataclass(frozen=True)
class Resultado:
    sucesso: bool
    mensagem: str
    identificador: str | None = None
    score: float | None = None
    etapas: Path | None = None


class SistemaBiometrico:
    """Uma instância mantém os cadastros durante a sessão local."""
    def __init__(self, configuracao=None, pasta_resultados=None):
        self._pipeline = PipelineFacial(configuracao, pasta_resultados)
        self._banco = BancoMemoria()

    def cadastrar_pessoa(self, identificador, caminho_imagem):
        identificador = self._banco.exigir_novo(identificador)
        amostra = self._pipeline.processar(caminho_imagem, identificador, 'cadastro')
        self._banco.adicionar(identificador, amostra)
        return Resultado(True, 'Pessoa cadastrada.', identificador, etapas=amostra.etapas)

    def autenticar_pessoa(self, identificador, caminho_imagem):
        identificador = validar_identificador(identificador)
        cadastrada = self._banco.obter(identificador)
        amostra = self._pipeline.processar(caminho_imagem, identificador, 'autenticacao')
        score = comparar(self._pipeline.reconhecedor.modelo, cadastrada.caracteristicas, amostra.caracteristicas)
        aceita = corresponde(score)
        return Resultado(aceita, 'Autenticado.' if aceita else 'Não autenticado.',
                         validar_identificador(identificador), score, amostra.etapas)
