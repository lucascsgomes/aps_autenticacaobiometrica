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
