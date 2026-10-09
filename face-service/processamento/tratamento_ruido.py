import cv2
import numpy as np
from processamento.validacao import validar_imagem, inteiro_positivo, par_positivo


def aplicar_filtro_mediano(imagem, kernel_size=5):
    validar_imagem(imagem)
    inteiro_positivo(kernel_size, "kernel_size", impar=True, minimo=3)
    return cv2.medianBlur(imagem, kernel_size)


def _morfologia(imagem, operacao, kernel_size, iterations):
    validar_imagem(imagem)
    kernel = par_positivo(kernel_size, "kernel_size")
    inteiro_positivo(iterations, "iterations")
    return cv2.morphologyEx(imagem, operacao, np.ones(kernel, np.uint8), iterations=iterations)


def aplicar_abertura_morfologica(imagem, kernel_size=(3, 3), iterations=1):
    return _morfologia(imagem, cv2.MORPH_OPEN, kernel_size, iterations)


def aplicar_fechamento_morfologico(imagem, kernel_size=(3, 3), iterations=1):
    return _morfologia(imagem, cv2.MORPH_CLOSE, kernel_size, iterations)


TRATAMENTOS_RUIDO = {
    "mediana": aplicar_filtro_mediano,
    "abertura": aplicar_abertura_morfologica,
    "fechamento": aplicar_fechamento_morfologico,
}
