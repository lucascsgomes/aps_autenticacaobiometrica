"""Serialização do vetor SFace: float32 little-endian em base64 (128 floats = 512 bytes)."""
import base64
import binascii

import numpy as np

from erros import ErroBiometria

DIM = 128


def codificar(vetor) -> str:
    return base64.b64encode(np.asarray(vetor, dtype='<f4').reshape(-1).tobytes()).decode('ascii')


def decodificar(texto: str) -> np.ndarray:
    try:
        bruto = base64.b64decode(texto, validate=True)
    except (binascii.Error, ValueError) as erro:
        raise ErroBiometria('Embedding de referência inválido.', codigo='INVALID_REFERENCE') from erro
    if len(bruto) != DIM * 4:
        raise ErroBiometria('Embedding de referência com tamanho inesperado.', codigo='INVALID_REFERENCE')
    vetor = np.frombuffer(bruto, dtype='<f4').astype(np.float32).reshape(1, DIM)
    if not np.isfinite(vetor).all():
        raise ErroBiometria('Embedding de referência com valores inválidos.', codigo='INVALID_REFERENCE')
    return vetor
