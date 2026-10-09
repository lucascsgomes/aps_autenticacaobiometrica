from dataclasses import dataclass
from pathlib import Path
import cv2
import numpy as np
from config import RAIZ, ConfiguracaoProcessamento
from erros import ErroBiometria, ErroInterno
from processamento.pre_processamento import PRE_PROCESSAMENTOS
from processamento.tratamento_ruido import TRATAMENTOS_RUIDO
from visualizacao.etapas import Etapas
from biometria.detector import DetectorFacial
from biometria.reconhecimento import ReconhecedorFacial


@dataclass
class AmostraFacial:
    caracteristicas: np.ndarray
    imagem: Path
    etapas: Path


def carregar_imagem(caminho):
    if not isinstance(caminho, (str, Path)) or not str(caminho).strip() or '\x00' in str(caminho):
        raise ErroBiometria('Caminho de imagem inválido.')
    arquivo = Path(caminho).expanduser()
    if not arquivo.exists():
        raise ErroBiometria(f'Arquivo inexistente: {arquivo}')
    if not arquivo.is_file():
        raise ErroBiometria(f'O caminho não é um arquivo: {arquivo}')
    # Leitura pelo Python preserva caminhos Unicode também no Windows.
    dados = np.frombuffer(arquivo.read_bytes(), dtype=np.uint8)
    imagem = cv2.imdecode(dados, cv2.IMREAD_COLOR) if dados.size else None
    if imagem is None or imagem.size == 0:
        raise ErroBiometria(f'OpenCV não conseguiu carregar a imagem: {arquivo}')
    return arquivo.resolve(), imagem


class PipelineFacial:
    def __init__(self, configuracao=None, pasta_resultados=None):
        self.configuracao = configuracao or ConfiguracaoProcessamento()
        self.configuracao.validar()
        self.pasta_resultados = Path(pasta_resultados) if pasta_resultados is not None else RAIZ / 'resultados'
        self.detector = DetectorFacial()
        self.reconhecedor = ReconhecedorFacial()

    def processar(self, caminho_imagem, identificador, operacao):
        etapas = None
        try:
            self.configuracao.validar()
            origem, original = carregar_imagem(caminho_imagem)
            etapas = Etapas(self.pasta_resultados, self.configuracao, origem, identificador, operacao)
            etapas.salvar('01_original', original)
            cinza = cv2.cvtColor(original, cv2.COLOR_BGR2GRAY)
            etapas.salvar('02_cinza', cinza)
            pre = PRE_PROCESSAMENTOS[self.configuracao.pre_processamento](cinza)
            etapas.salvar('03_pre_processamento', pre)
            processada = pre
            if self.configuracao.tratamento_ruido is not None:
                processada = TRATAMENTOS_RUIDO[self.configuracao.tratamento_ruido](pre)
                etapas.salvar('04_tratamento_ruido', processada)
            entrada = cv2.cvtColor(processada, cv2.COLOR_GRAY2BGR)
            faces = self.detector.detectar(entrada)
            etapas.salvar_deteccao(entrada, faces)
            if len(faces) == 0:
                raise ErroBiometria('Nenhum rosto encontrado. Tire outra foto de frente, com o rosto alinhado e próximo à câmera.')
            if len(faces) != 1:
                raise ErroBiometria(f'Mais de uma face encontrada ({len(faces)}); é necessário exatamente um rosto.')
            # A detecção e o alinhamento usam a mesma imagem, sem desenhos.
            alinhado = self.reconhecedor.alinhar(entrada, faces[0])
            etapas.salvar('06_rosto_alinhado', alinhado)
            vetor = self.reconhecedor.extrair(alinhado)
            etapas.registrar('Extração concluída. Características mantidas somente em memória.')
            return AmostraFacial(vetor, origem, etapas.pasta)
        except (ErroBiometria, OSError, cv2.error) as erro:
            if etapas is not None:
                try:
                    etapas.registrar(f'Erro: {erro}')
                except OSError:
                    pass
            if isinstance(erro, ErroBiometria):
                raise
            raise ErroInterno(str(erro)) from erro
