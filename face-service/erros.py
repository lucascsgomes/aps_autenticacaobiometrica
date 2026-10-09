class ErroBiometria(Exception):
    """Falha esperada de entrada, configuração ou processamento biométrico."""


class ErroInterno(ErroBiometria):
    """Detalhe técnico preservado internamente, sem exposição no terminal."""
