from erros import ErroBiometria


def validar_identificador(identificador):
    if not isinstance(identificador, str) or not identificador.strip():
        raise ErroBiometria('Identificador deve ser um texto não vazio.')
    return identificador.strip()


class BancoMemoria:
    def __init__(self):
        self._pessoas = {}

    def exigir_novo(self, identificador):
        identificador = validar_identificador(identificador)
        if identificador in self._pessoas:
            raise ErroBiometria(f'Identificador já cadastrado: {identificador}')
        return identificador

    def adicionar(self, identificador, amostra):
        identificador = self.exigir_novo(identificador)
        self._pessoas[identificador] = amostra

    def obter(self, identificador):
        identificador = validar_identificador(identificador)
        if identificador not in self._pessoas:
            raise ErroBiometria(f'Identificador não cadastrado: {identificador}')
        return self._pessoas[identificador]

    def todos(self):
        """Lista `(identificador, características)` de todas as pessoas cadastradas, para a identificação 1 para N."""
        return [(identificador, amostra.caracteristicas) for identificador, amostra in self._pessoas.items()]
