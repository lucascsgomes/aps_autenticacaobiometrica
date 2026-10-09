import cv2
import numpy as np
from config import RAIZ
from erros import ErroInterno
from biometria.modelos import verificar_modelo

MODELO_SFACE = RAIZ / 'modelos/face_recognition_sface_2021dec.onnx'


class ReconhecedorFacial:
    def __init__(self):
        caminho = verificar_modelo(MODELO_SFACE)
        try:
            self.modelo = cv2.FaceRecognizerSF.create(caminho, '')
        except (cv2.error, AttributeError) as erro:
            raise ErroInterno('Modelo SFace inválido ou OpenCV incompatível.') from erro

    def alinhar(self, imagem_bgr, face):
        try:
            return self.modelo.alignCrop(imagem_bgr, face)
        except cv2.error as erro:
            raise ErroInterno('Falha no alinhamento facial SFace.') from erro

    def extrair(self, rosto_alinhado):
        try:
            vetor = self.modelo.feature(rosto_alinhado).copy()
        except cv2.error as erro:
            raise ErroInterno('Falha na extração de características SFace.') from erro
        if vetor.size == 0 or not np.isfinite(vetor).all() or np.linalg.norm(vetor) == 0:
            raise ErroInterno('SFace retornou características inválidas.')
        return vetor
