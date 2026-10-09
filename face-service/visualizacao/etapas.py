import re
import cv2
from erros import ErroBiometria


class Etapas:
    def __init__(self, raiz, configuracao, origem, identificador, operacao):
        # Remove separadores e caracteres proibidos pelo Windows; nunca usa
        # o identificador como caminho diretamente.
        def nome_seguro(valor):
            nome = re.sub(r'[^\w-]+', '_', str(valor), flags=re.UNICODE).strip('_')
            return nome[:60] or 'sem_nome'

        nome = '__'.join(nome_seguro(valor) for valor in (origem.stem, identificador, operacao))
        raiz.mkdir(parents=True, exist_ok=True)
        numero = 1
        while True:
            sufixo = '' if numero == 1 else f'__{numero}'
            self.pasta = raiz / (nome + sufixo)
            try:
                self.pasta.mkdir(exist_ok=False)
                break
            except FileExistsError:
                numero += 1
        self.registrar(f'Imagem: {origem}\nIdentificador: {identificador}\nOperação: {operacao}\n'
                       f'Pré-processamento: {configuracao.pre_processamento}\n'
                       f'Ruído: {configuracao.tratamento_ruido or "desativado (etapa pulada)"}\nOpenCV: {cv2.__version__}\n')

    def registrar(self, texto):
        with (self.pasta / 'execucao.txt').open('a', encoding='utf-8') as arquivo:
            arquivo.write(texto + '\n')

    def salvar(self, nome, imagem):
        sucesso, dados = cv2.imencode('.png', imagem)
        if not sucesso:
            raise ErroBiometria(f'Não foi possível salvar etapa {nome} em {self.pasta}.')
        (self.pasta / f'{nome}.png').write_bytes(dados.tobytes())

    def salvar_deteccao(self, imagem_bgr, faces):
        desenho = imagem_bgr.copy()
        for face in faces:
            x, y, largura, altura = map(round, face[:4])
            cv2.rectangle(desenho, (x, y), (x + largura, y + altura), (0, 255, 0), 2)
            for px, py in face[4:14].reshape(5, 2):
                cv2.circle(desenho, (round(float(px)), round(float(py))), 3, (0, 0, 255), -1)
        self.salvar('05_deteccao', desenho)
