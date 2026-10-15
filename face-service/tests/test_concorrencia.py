import threading
import time
from concurrent.futures import ThreadPoolExecutor

import numpy as np

from biometria.pipeline import PipelineFacial
from config import ConfiguracaoProcessamento
from visualizacao.etapas import EtapasNulas


class DetectorQueMedeSobreposicao:
    """Finge o YuNet e anota quantas fotos estavam dentro dele ao mesmo tempo."""

    def __init__(self):
        self.dentro = 0
        self.maximo = 0
        self._contador = threading.Lock()

    def detectar(self, _imagem):
        with self._contador:
            self.dentro += 1
            self.maximo = max(self.maximo, self.dentro)
        time.sleep(0.02)  # dá tempo de outra thread entrar, se nada a impedir
        with self._contador:
            self.dentro -= 1
        return np.zeros((1, 15), dtype=np.float32)


class ReconhecedorFalso:
    @staticmethod
    def alinhar(imagem, _face):
        return imagem

    @staticmethod
    def extrair(_alinhado):
        return np.zeros((1, 128), dtype=np.float32)


def test_so_uma_foto_por_vez_usa_os_modelos():
    pipeline = PipelineFacial.__new__(PipelineFacial)  # sem carregar os modelos de verdade
    pipeline.configuracao = ConfiguracaoProcessamento()
    pipeline.detector = DetectorQueMedeSobreposicao()
    pipeline.reconhecedor = ReconhecedorFalso()
    imagem = np.zeros((64, 64, 3), dtype=np.uint8)

    with ThreadPoolExecutor(8) as pool:
        list(pool.map(lambda _: pipeline._extrair(imagem, EtapasNulas()), range(16)))

    assert pipeline.detector.maximo == 1
