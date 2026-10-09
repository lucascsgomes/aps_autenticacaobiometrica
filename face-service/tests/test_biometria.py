import base64

import pytest

from erros import ErroBiometria
from tests.conftest import CHAVE, PipelineFalso, b64, png_valido, vetor_aleatorio


def enviar_imagem(imagem=None):
    return {'image': ('foto.png', imagem if imagem is not None else png_valido(), 'image/png')}


# --- autenticação serviço a serviço ---

@pytest.mark.parametrize('rota', ['/internal/v1/face/enroll', '/internal/v1/face/verify'])
def test_rotas_biometricas_exigem_chave_interna(cliente, usar_pipeline, rota):
    usar_pipeline(PipelineFalso())

    sem_chave = cliente.post(rota, files=enviar_imagem(), data={'reference_b64': b64(vetor_aleatorio(1))})
    chave_errada = cliente.post(rota, files=enviar_imagem(), headers={'X-Internal-Key': 'errada'},
                                data={'reference_b64': b64(vetor_aleatorio(1))})

    assert sem_chave.status_code == chave_errada.status_code == 401
    assert sem_chave.json()['code'] == 'UNAUTHORIZED'


def test_sem_chave_configurada_no_servidor_tudo_fica_fechado(cliente, usar_pipeline, monkeypatch):
    monkeypatch.delenv('FACE_INTERNAL_KEY')
    usar_pipeline(PipelineFalso())

    resposta = cliente.post('/internal/v1/face/enroll', files=enviar_imagem(), headers={'X-Internal-Key': ''})

    assert resposta.status_code == 401


def test_chave_anterior_ainda_vale_durante_a_rotacao(cliente, usar_pipeline, monkeypatch):
    monkeypatch.setenv('FACE_INTERNAL_KEY', 'nova')
    monkeypatch.setenv('FACE_INTERNAL_KEY_PREV', 'antiga')
    usar_pipeline(PipelineFalso())

    for chave in ('nova', 'antiga'):
        resposta = cliente.post('/internal/v1/face/enroll', files=enviar_imagem(), headers={'X-Internal-Key': chave})
        assert resposta.status_code == 200


# --- enroll ---

def test_enroll_devolve_embedding_de_128_floats_em_base64(cliente, cabecalho, usar_pipeline):
    usar_pipeline(PipelineFalso())

    resposta = cliente.post('/internal/v1/face/enroll', files=enviar_imagem(), headers=cabecalho)

    corpo = resposta.json()
    assert resposta.status_code == 200
    assert corpo['dim'] == 128
    assert len(base64.b64decode(corpo['embedding_b64'])) == 512
    assert corpo['faces_detected'] == 1


@pytest.mark.parametrize(('codigo', 'status'), [('NO_FACE_DETECTED', 422), ('MULTIPLE_FACES', 422)])
def test_enroll_traduz_erros_de_rosto(cliente, cabecalho, usar_pipeline, codigo, status):
    usar_pipeline(PipelineFalso(erro=ErroBiometria('x', codigo=codigo)))

    resposta = cliente.post('/internal/v1/face/enroll', files=enviar_imagem(), headers=cabecalho)

    assert resposta.status_code == status
    assert resposta.json()['code'] == codigo
    assert resposta.headers['content-type'].startswith('application/problem+json')


def test_enroll_recusa_arquivo_que_nao_e_imagem(cliente, cabecalho, usar_pipeline):
    usar_pipeline(PipelineFalso())

    resposta = cliente.post('/internal/v1/face/enroll', files=enviar_imagem(b'isto nao e uma imagem'),
                            headers=cabecalho)

    assert resposta.status_code == 400
    assert resposta.json()['code'] == 'INVALID_IMAGE'


def test_enroll_recusa_imagem_acima_de_2mb(cliente, cabecalho, usar_pipeline):
    usar_pipeline(PipelineFalso())

    resposta = cliente.post('/internal/v1/face/enroll', files=enviar_imagem(b'0' * (2 * 1024 * 1024 + 1)),
                            headers=cabecalho)

    assert resposta.status_code == 413
    assert resposta.json()['code'] == 'IMAGE_TOO_LARGE'


def test_enroll_sem_campo_image_e_400(cliente, cabecalho, usar_pipeline):
    usar_pipeline(PipelineFalso())

    resposta = cliente.post('/internal/v1/face/enroll', headers=cabecalho)

    assert resposta.status_code == 400
    assert resposta.json()['code'] == 'INVALID_REQUEST'


def test_erro_interno_nao_vaza_detalhe_tecnico(cliente, cabecalho, usar_pipeline):
    usar_pipeline(PipelineFalso(erro=ErroBiometria('caminho C:/segredo/modelo.onnx quebrou')))

    resposta = cliente.post('/internal/v1/face/enroll', files=enviar_imagem(), headers=cabecalho)

    assert resposta.status_code == 500
    assert 'segredo' not in resposta.text


# --- verify ---

def verificar(cliente, cabecalho, referencia, **extra):
    return cliente.post('/internal/v1/face/verify', files=enviar_imagem(), headers=cabecalho,
                        data={'reference_b64': b64(referencia), **extra})


def test_verify_mesma_pessoa_da_match_com_limiar_padrao(cliente, cabecalho, usar_pipeline):
    rosto = vetor_aleatorio(1)
    usar_pipeline(PipelineFalso(vetor=rosto))

    corpo = verificar(cliente, cabecalho, rosto).json()

    assert corpo['match'] is True
    assert corpo['score'] == pytest.approx(1.0)
    assert corpo['threshold'] == 0.363
    assert corpo['metric'] == 'cosine'


def test_verify_pessoa_diferente_e_200_com_match_falso(cliente, cabecalho, usar_pipeline):
    usar_pipeline(PipelineFalso(vetor=vetor_aleatorio(1)))

    resposta = verificar(cliente, cabecalho, vetor_aleatorio(2))

    # Recusa biométrica não é falha de serviço: precisa ser 200.
    assert resposta.status_code == 200
    assert resposta.json()['match'] is False


def test_verify_respeita_limiar_enviado_pelo_chamador(cliente, cabecalho, usar_pipeline):
    rosto = vetor_aleatorio(1)
    usar_pipeline(PipelineFalso(vetor=rosto))

    corpo = verificar(cliente, cabecalho, rosto, threshold='0.9').json()

    assert corpo['threshold'] == 0.9
    assert corpo['match'] is True


@pytest.mark.parametrize('limiar', ['0.299', '0.901', '-1'])
def test_verify_recusa_limiar_fora_da_faixa(cliente, cabecalho, usar_pipeline, limiar):
    usar_pipeline(PipelineFalso())

    resposta = verificar(cliente, cabecalho, vetor_aleatorio(1), threshold=limiar)

    assert resposta.status_code == 400
    assert resposta.json()['code'] == 'INVALID_THRESHOLD'


@pytest.mark.parametrize('referencia', ['não-é-base64!!', base64.b64encode(b'curto').decode()])
def test_verify_recusa_referencia_invalida(cliente, cabecalho, usar_pipeline, referencia):
    usar_pipeline(PipelineFalso())

    resposta = cliente.post('/internal/v1/face/verify', files=enviar_imagem(), headers=cabecalho,
                            data={'reference_b64': referencia})

    assert resposta.status_code == 400
    assert resposta.json()['code'] == 'INVALID_REFERENCE'


def test_chave_de_teste_nao_vaza_na_resposta(cliente, cabecalho, usar_pipeline):
    usar_pipeline(PipelineFalso())

    resposta = cliente.post('/internal/v1/face/enroll', files=enviar_imagem(), headers=cabecalho)

    assert CHAVE not in resposta.text
