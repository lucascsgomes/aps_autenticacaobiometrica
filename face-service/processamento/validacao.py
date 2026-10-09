import math
import numpy as np
from erros import ErroInterno


def validar_imagem(imagem):
    if (not isinstance(imagem, np.ndarray) or imagem.dtype != np.uint8
            or imagem.ndim != 2 or imagem.size == 0):
        raise ErroInterno("O filtro exige imagem grayscale não vazia, uint8.")


def inteiro_positivo(valor, nome, impar=False, minimo=1):
    if (type(valor) is not int or valor < minimo or (impar and valor % 2 == 0)):
        raise ErroInterno(f"{nome} deve ser inteiro >= {minimo}" + (" e ímpar." if impar else "."))


def par_positivo(valor, nome, impar=False):
    if not isinstance(valor, (tuple, list)) or len(valor) != 2:
        raise ErroInterno(f"{nome} deve conter dois inteiros positivos.")
    for item in valor:
        inteiro_positivo(item, nome, impar)
    return tuple(valor)


def numero_finito(valor, nome, estritamente_positivo=False):
    if (isinstance(valor, bool) or not isinstance(valor, (int, float))
            or not math.isfinite(valor) or valor < 0
            or (estritamente_positivo and valor == 0)):
        raise ErroInterno(f"{nome} deve ser finito e " + ("positivo." if estritamente_positivo else "não negativo."))
