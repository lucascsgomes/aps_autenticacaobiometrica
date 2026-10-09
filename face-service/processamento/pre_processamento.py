import cv2
from processamento.validacao import validar_imagem, par_positivo, numero_finito


def aplicar_clahe(imagem, clip_limit=2.0, grid_size=(8, 8)):
    validar_imagem(imagem)
    numero_finito(clip_limit, "clip_limit", estritamente_positivo=True)
    grade = par_positivo(grid_size, "grid_size")
    return cv2.createCLAHE(clipLimit=clip_limit, tileGridSize=grade).apply(imagem)


def aplicar_equalizacao_histograma(imagem):
    validar_imagem(imagem)
    return cv2.equalizeHist(imagem)


def aplicar_filtro_gaussiano(imagem, kernel_size=(5, 5), sigma=0):
    validar_imagem(imagem)
    kernel = par_positivo(kernel_size, "kernel_size", impar=True)
    numero_finito(sigma, "sigma")
    return cv2.GaussianBlur(imagem, kernel, sigma)


PRE_PROCESSAMENTOS = {
    "clahe": aplicar_clahe,
    "equalizacao": aplicar_equalizacao_histograma,
    "gaussiano": aplicar_filtro_gaussiano,
}
