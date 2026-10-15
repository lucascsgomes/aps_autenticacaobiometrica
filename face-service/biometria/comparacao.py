import math
import cv2
from erros import ErroBiometria

# Decisão biométrica fixa; não pertence à configuração de processamento.
THRESHOLD_COSINE = 0.363


def comparar(modelo_sface, caracteristicas_a, caracteristicas_b):
    try:
        # match pode normalizar os vetores; preservamos os vetores guardados com cópias.
        score = float(modelo_sface.match(caracteristicas_a.copy(), caracteristicas_b.copy(),
                                         cv2.FaceRecognizerSF_FR_COSINE))
    except cv2.error as erro:
        raise ErroBiometria('Falha na comparação SFace/Cosine.') from erro
    if not math.isfinite(score):
        raise ErroBiometria('SFace retornou score inválido.')
    return score


def corresponde(score, limiar=THRESHOLD_COSINE):
    return score >= limiar
