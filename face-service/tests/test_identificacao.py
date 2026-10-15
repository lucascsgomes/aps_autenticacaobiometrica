import json

import numpy as np
import pytest

from banco.memoria import BancoMemoria
from biometria.pipeline import AmostraFacial
from config import LIMITE_CANDIDATOS
from erros import ErroBiometria
from sistema import SistemaBiometrico
from tests.conftest import ModeloFalso, PipelineFalso, b64, png_valido, vetor_aleatorio

ROTA = '/internal/v1/face/identify'


def candidatos(**vetores):
    return json.dumps([{'id': id_, 'embedding_b64': b64(v)} for id_, v in vetores.items()])


def identificar(cliente, cabecalho, lista, **extras):
    return cliente.post(ROTA, headers=cabecalho, data={'candidates': lista, **extras},
                        files={'image': ('foto.png', png_valido(), 'image/png')})


def test_identifica_o_candidato_certo_entre_varios(cliente, cabecalho, usar_pipeline):
    usar_pipeline(PipelineFalso(vetor_aleatorio(2)))

    todos = candidatos(ana=vetor_aleatorio(1), bia=vetor_aleatorio(2), caio=vetor_aleatorio(3))
    r = identificar(cliente, cabecalho, todos)

    corpo = r.json()
    assert r.status_code == 200
    assert corpo['identified'] is True and corpo['candidate_id'] == 'bia'
    assert corpo['score'] == pytest.approx(1.0, abs=1e-4)
    assert corpo['candidates_compared'] == 3 and corpo['ambiguous'] is False


def test_ninguem_acima_do_limiar_nao_devolve_id_nenhum(cliente, cabecalho, usar_pipeline):
    usar_pipeline(PipelineFalso(vetor_aleatorio(99)))

    corpo = identificar(cliente, cabecalho, candidatos(ana=vetor_aleatorio(1), bia=vetor_aleatorio(2))).json()

    assert corpo['identified'] is False and corpo['candidate_id'] is None
    assert corpo['score'] < corpo['threshold']  # o score fica para o relatório interno, o id não vaza


def test_dois_candidatos_iguais_sao_ambiguos_e_a_margem_pode_ser_zerada(cliente, cabecalho, usar_pipeline):
    usar_pipeline(PipelineFalso(vetor_aleatorio(1)))
    gemeos = candidatos(a=vetor_aleatorio(1), b=vetor_aleatorio(1))

    padrao = identificar(cliente, cabecalho, gemeos).json()
    sem_margem = identificar(cliente, cabecalho, gemeos, margin='0').json()

    assert padrao['identified'] is False and padrao['ambiguous'] is True and padrao['candidate_id'] is None
    # empate: o id menor vence, sempre igual
    assert sem_margem['identified'] is True and sem_margem['candidate_id'] == 'a'


def test_o_limiar_enviado_vale(cliente, cabecalho, usar_pipeline):
    foto = vetor_aleatorio(1)
    parecido = foto + 0.8 * vetor_aleatorio(2)  # cosseno por volta de 0,78
    usar_pipeline(PipelineFalso(foto))

    com_padrao = identificar(cliente, cabecalho, candidatos(ana=parecido)).json()
    rigoroso = identificar(cliente, cabecalho, candidatos(ana=parecido), threshold='0.9').json()

    assert com_padrao['identified'] is True
    assert rigoroso['identified'] is False


@pytest.mark.parametrize('lista', [
    pytest.param('isto não é json', id='nao_e_json'),
    pytest.param('{"id": "a"}', id='nao_e_lista'),
    pytest.param('[]', id='lista_vazia'),
    pytest.param('[{"id": "a"}]', id='sem_vetor'),
    pytest.param('[{"id": "", "embedding_b64": "x"}]', id='id_vazio'),
    pytest.param(json.dumps([{'id': 'a', 'embedding_b64': b64(vetor_aleatorio(1))}] * 2), id='id_repetido'),
    pytest.param(json.dumps([{'id': f'p{i}', 'embedding_b64': 'x'} for i in range(LIMITE_CANDIDATOS + 1)]),
                 id='acima_do_limite'),
])
def test_candidatos_malformados_voltam_400(cliente, cabecalho, usar_pipeline, lista):
    usar_pipeline(PipelineFalso())

    r = identificar(cliente, cabecalho, lista)

    assert r.status_code == 400 and r.json()['code'] == 'INVALID_CANDIDATES'


def test_vetor_de_candidato_corrompido_volta_400(cliente, cabecalho, usar_pipeline):
    usar_pipeline(PipelineFalso())

    r = identificar(cliente, cabecalho, json.dumps([{'id': 'a', 'embedding_b64': 'isso-nao-e-base64'}]))

    assert r.status_code == 400 and r.json()['code'] == 'INVALID_REFERENCE'


@pytest.mark.parametrize('extra, codigo', [
    ({'threshold': '0.1'}, 'INVALID_THRESHOLD'),
    ({'margin': '0.9'}, 'INVALID_MARGIN'),
    ({'margin': '-0.1'}, 'INVALID_MARGIN'),
])
def test_limiar_e_margem_fora_da_faixa_voltam_400(cliente, cabecalho, usar_pipeline, extra, codigo):
    usar_pipeline(PipelineFalso())

    r = identificar(cliente, cabecalho, candidatos(ana=vetor_aleatorio(1)), **extra)

    assert r.status_code == 400 and r.json()['code'] == codigo


def test_foto_sem_rosto_volta_422_como_nas_outras_rotas(cliente, cabecalho, usar_pipeline):
    usar_pipeline(PipelineFalso(erro=ErroBiometria('sem rosto', codigo='NO_FACE_DETECTED')))

    r = identificar(cliente, cabecalho, candidatos(ana=vetor_aleatorio(1)))

    assert r.status_code == 422 and r.json()['code'] == 'NO_FACE_DETECTED'


def test_a_rota_exige_a_chave_interna(cliente, usar_pipeline):
    usar_pipeline(PipelineFalso())

    r = cliente.post(ROTA, data={'candidates': candidatos(ana=vetor_aleatorio(1))},
                     files={'image': ('foto.png', png_valido(), 'image/png')})

    assert r.status_code == 401


# --- menu de terminal (a ferramenta em Python): identificar sem dizer quem é ---

class PipelineDeTerminal(PipelineFalso):
    def __init__(self, vetor):
        super().__init__(vetor)
        self.reconhecedor.modelo = ModeloFalso()

    def processar(self, _caminho, _identificador, _operacao):
        return AmostraFacial(self.vetor, None, None)


def sistema_com(*pessoas, foto):
    sistema = SistemaBiometrico.__new__(SistemaBiometrico)  # sem carregar os modelos de verdade
    sistema._pipeline = PipelineDeTerminal(foto)
    sistema._banco = BancoMemoria()
    for nome, vetor in pessoas:
        sistema._banco.adicionar(nome, AmostraFacial(vetor, None, None))
    return sistema


def test_terminal_identifica_quem_e_sem_informar_o_nome():
    sistema = sistema_com(('ana', vetor_aleatorio(1)), ('bia', vetor_aleatorio(2)), foto=vetor_aleatorio(2))

    r = sistema.identificar_pessoa('qualquer.jpg')

    assert r.sucesso and r.identificador == 'bia'


def test_terminal_recusa_quem_nao_esta_cadastrado():
    sistema = sistema_com(('ana', vetor_aleatorio(1)), foto=vetor_aleatorio(50))

    r = sistema.identificar_pessoa('qualquer.jpg')

    assert not r.sucesso and r.identificador is None and 'Ninguém' in r.mensagem


def test_terminal_sem_ninguem_cadastrado_explica_o_erro():
    sistema = sistema_com(foto=np.zeros((1, 128), dtype=np.float32))

    with pytest.raises(ErroBiometria, match='Nenhuma pessoa'):
        sistema.identificar_pessoa('qualquer.jpg')
