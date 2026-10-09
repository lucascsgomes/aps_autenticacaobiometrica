"""Entrada de imagem do serviço: valida tamanho, decodifica bytes e reduz a largura."""
import cv2
import numpy as np

from config import LARGURA_MAXIMA_PX, LIMITE_IMAGEM_BYTES
from erros import ErroBiometria


def decodificar_imagem(dados: bytes):
    if len(dados) > LIMITE_IMAGEM_BYTES:
        raise ErroBiometria('Imagem acima de 2 MB.', codigo='IMAGE_TOO_LARGE')
    # Não confia em Content-Type nem extensão: só o que o OpenCV consegue decodificar.
    imagem = cv2.imdecode(np.frombuffer(dados, dtype=np.uint8), cv2.IMREAD_COLOR) if dados else None
    if imagem is None or imagem.size == 0:
        raise ErroBiometria('Arquivo não é uma imagem válida.', codigo='INVALID_IMAGE')
    return reduzir(imagem)


def reduzir(imagem):
    altura, largura = imagem.shape[:2]
    if largura <= LARGURA_MAXIMA_PX:
        return imagem
    fator = LARGURA_MAXIMA_PX / largura
    return cv2.resize(imagem, (LARGURA_MAXIMA_PX, round(altura * fator)), interpolation=cv2.INTER_AREA)
