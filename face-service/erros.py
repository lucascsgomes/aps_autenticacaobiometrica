class ErroBiometria(Exception):
    """Falha esperada de entrada, configuração ou processamento biométrico.

    `codigo` é o identificador estável usado pelo serviço HTTP (ex.: NO_FACE_DETECTED).
    A CLI continua usando só a mensagem.
    """

    def __init__(self, mensagem, codigo=None):
        super().__init__(mensagem)
        self.codigo = codigo


class ErroInterno(ErroBiometria):
    """Detalhe técnico preservado internamente, sem exposição no terminal."""
