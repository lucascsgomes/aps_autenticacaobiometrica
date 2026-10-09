import cv2
import numpy as np
import pytest
from fastapi.testclient import TestClient

from app import embedding
from app.main import app, obter_pipeline

CHAVE = 'chave-de-teste'


class ModeloFalso:
    """Faz o papel do SFace: cosseno entre dois vetores."""

    def match(self, a, b, _flag):
        a, b = a.reshape(-1), b.reshape(-1)
        return float(a @ b / (np.linalg.norm(a) * np.linalg.norm(b)))


class ReconhecedorFalso:
    modelo = ModeloFalso()


class PipelineFalso:
    """Devolve sempre o mesmo rosto, ou levanta o erro configurado. Não usa modelos de verdade."""

    reconhecedor = ReconhecedorFalso()

    def __init__(self, vetor=None, erro=None):
        self.vetor = vetor if vetor is not None else vetor_aleatorio(1)
        self.erro = erro

    def extrair_de_imagem(self, _imagem):
        if self.erro:
            raise self.erro
        return self.vetor


def vetor_aleatorio(semente):
    return np.random.default_rng(semente).normal(size=(1, 128)).astype(np.float32)


def png_valido(largura=64, altura=64):
    ruido = np.random.default_rng(0).integers(0, 255, (altura, largura, 3), dtype=np.uint8)
    return cv2.imencode('.png', ruido)[1].tobytes()


@pytest.fixture(autouse=True)
def chave_interna(monkeypatch):
    monkeypatch.setenv('FACE_INTERNAL_KEY', CHAVE)
    monkeypatch.delenv('FACE_INTERNAL_KEY_PREV', raising=False)


@pytest.fixture
def usar_pipeline():
    """Troca o pipeline real pelo falso durante o teste."""

    def _usar(pipeline):
        app.dependency_overrides[obter_pipeline] = lambda: pipeline

    yield _usar
    app.dependency_overrides.clear()


@pytest.fixture
def cliente():
    # Sem `with`: o lifespan (carregar modelos reais) não roda nos testes de lógica.
    return TestClient(app)


@pytest.fixture
def cabecalho():
    return {'X-Internal-Key': CHAVE}


def b64(vetor):
    return embedding.codificar(vetor)
