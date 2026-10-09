import cv2
import numpy as np
from config import RAIZ
from erros import ErroInterno
from biometria.modelos import verificar_modelo

MODELO_YUNET = RAIZ / 'modelos/face_detection_yunet_2023mar.onnx'


class DetectorFacial:
    def __init__(self):
        caminho = verificar_modelo(MODELO_YUNET)
        try:
            self.modelo = cv2.FaceDetectorYN.create(caminho, '', (320, 320), 0.6, 0.3, 5000)
        except (cv2.error, AttributeError) as erro:
            raise ErroInterno('Modelo YuNet inválido ou OpenCV incompatível.') from erro

    def detectar(self, imagem_bgr):
        altura, largura = imagem_bgr.shape[:2]
        try:
            self.modelo.setInputSize((largura, altura))
            _, faces = self.modelo.detect(imagem_bgr)
        except cv2.error as erro:
            raise ErroInterno('Falha na inferência YuNet; verifique modelo e imagem.') from erro
        if faces is None:
            return np.empty((0, 15), dtype=np.float32)
        if faces.ndim != 2 or faces.shape[1] != 15 or not np.isfinite(faces).all():
            raise ErroInterno('YuNet retornou detecções inválidas; esperado N x 15.')
        return faces
