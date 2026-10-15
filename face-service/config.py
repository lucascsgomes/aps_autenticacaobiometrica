import os
from dataclasses import dataclass
from pathlib import Path

RAIZ = Path(__file__).resolve().parent
PRE_PROCESSAMENTO_ATUAL = "clahe"
TRATAMENTO_RUIDO_ATUAL = None  # Pula o tratamento de ruído.


@dataclass(frozen=True)
class ConfiguracaoProcessamento:
    pre_processamento: str = PRE_PROCESSAMENTO_ATUAL
    tratamento_ruido: str | None = TRATAMENTO_RUIDO_ATUAL

    def validar(self):
        from processamento.pre_processamento import PRE_PROCESSAMENTOS
        from processamento.tratamento_ruido import TRATAMENTOS_RUIDO
        from erros import ErroBiometria

        for valor, opcoes in ((self.pre_processamento, PRE_PROCESSAMENTOS),
                              (self.tratamento_ruido, TRATAMENTOS_RUIDO)):
            if valor is None and opcoes is TRATAMENTOS_RUIDO:
                continue
            if not isinstance(valor, str) or valor not in opcoes:
                raise ErroBiometria(f"Configuração inexistente: {valor!r}. Opções: {', '.join(opcoes)}")


# --- Configuração do serviço HTTP (lida do ambiente, nunca do código) ---
LIMITE_IMAGEM_BYTES = 2 * 1024 * 1024
LARGURA_MAXIMA_PX = 640
LIMIAR_MINIMO, LIMIAR_MAXIMO = 0.300, 0.900
MARGEM_MAXIMA = 0.500
# Cada candidato ocupa cerca de 690 caracteres no formulário, e o corpo de um campo de texto não passa de 1 MB.
LIMITE_CANDIDATOS = 1000


def chaves_internas():
    """Chave atual e (opcional) anterior, para rotação sem parada. Vazio = rotas biométricas fechadas."""
    return [k for k in (os.environ.get("FACE_INTERNAL_KEY"), os.environ.get("FACE_INTERNAL_KEY_PREV")) if k]
