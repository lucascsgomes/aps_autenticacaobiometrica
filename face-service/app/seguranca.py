"""Autenticação serviço a serviço por X-Internal-Key."""
import secrets

from fastapi import Header

from config import chaves_internas


class NaoAutorizado(Exception):
    pass


def exigir_chave_interna(x_internal_key: str | None = Header(default=None)):
    chaves = chaves_internas()
    # Sem chave configurada, falha fechada: nenhuma rota biométrica responde.
    if not x_internal_key or not any(secrets.compare_digest(x_internal_key, k) for k in chaves):
        raise NaoAutorizado
