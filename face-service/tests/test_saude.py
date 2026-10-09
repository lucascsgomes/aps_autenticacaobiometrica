from pathlib import Path

import pytest
from fastapi.testclient import TestClient

from app.main import app
from config import RAIZ

MODELOS = [RAIZ / 'modelos/face_detection_yunet_2023mar.onnx', RAIZ / 'modelos/face_recognition_sface_2021dec.onnx']


def test_health_responde_vivo_sem_autenticacao(cliente):
    resposta = cliente.get('/internal/v1/health')

    assert resposta.status_code == 200
    assert resposta.json() == {'status': 'vivo'}


def test_ready_devolve_503_quando_modelos_nao_carregaram(cliente):
    app.state.pipeline = None

    resposta = cliente.get('/internal/v1/ready')

    assert resposta.status_code == 503
    assert resposta.headers['content-type'].startswith('application/problem+json')
    assert resposta.json()['code'] == 'MODELS_NOT_LOADED'


@pytest.mark.skipif(not all(Path(m).is_file() for m in MODELOS), reason='modelos .onnx não baixados')
def test_ready_com_os_modelos_reais_carregados():
    with TestClient(app) as cliente_com_ciclo_de_vida:
        resposta = cliente_com_ciclo_de_vida.get('/internal/v1/ready')

    assert resposta.status_code == 200
    assert resposta.json() == {'yunet': 'ok', 'sface': 'ok', 'model': 'sface-2021dec'}


def test_request_id_enviado_e_devolvido(cliente):
    resposta = cliente.get('/internal/v1/health', headers={'X-Request-Id': 'abc-123'})

    assert resposta.headers['x-request-id'] == 'abc-123'


def test_request_id_e_gerado_quando_ausente(cliente):
    resposta = cliente.get('/internal/v1/health')

    assert len(resposta.headers['x-request-id']) == 36
