"""O admin escolhe o pré-processamento e o tratamento de ruído; a API repassa a escolha a cada chamada."""
import pytest

from config import ConfiguracaoProcessamento
from tests.conftest import PipelineFalso
from tests.test_biometria import enviar_imagem

ROTA = '/internal/v1/face/enroll'


def test_sem_campos_vale_a_configuracao_do_servico(cliente, cabecalho, usar_pipeline):
    pipeline = PipelineFalso()
    usar_pipeline(pipeline)

    assert cliente.post(ROTA, files=enviar_imagem(), headers=cabecalho).status_code == 200

    assert pipeline.configuracao_recebida is None


def test_escolha_do_admin_chega_ao_pipeline(cliente, cabecalho, usar_pipeline):
    pipeline = PipelineFalso()
    usar_pipeline(pipeline)

    campos = {'preprocessing': 'gaussiano', 'noise': 'mediana'}
    resposta = cliente.post(ROTA, files=enviar_imagem(), data=campos, headers=cabecalho)

    assert resposta.status_code == 200
    assert pipeline.configuracao_recebida == ConfiguracaoProcessamento('gaussiano', 'mediana')


def test_noise_none_desliga_o_ruido(cliente, cabecalho, usar_pipeline):
    pipeline = PipelineFalso()
    usar_pipeline(pipeline)

    campos = {'preprocessing': 'equalizacao', 'noise': 'none'}
    cliente.post(ROTA, files=enviar_imagem(), data=campos, headers=cabecalho)

    assert pipeline.configuracao_recebida == ConfiguracaoProcessamento('equalizacao', None)


@pytest.mark.parametrize('campos', [{'preprocessing': 'inexistente'}, {'noise': 'inexistente'}])
def test_opcao_desconhecida_e_400(cliente, cabecalho, usar_pipeline, campos):
    usar_pipeline(PipelineFalso())

    resposta = cliente.post(ROTA, files=enviar_imagem(), data=campos, headers=cabecalho)

    assert resposta.status_code == 400
    assert resposta.json()['code'] == 'INVALID_PROCESSING'
