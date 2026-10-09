"""Cadastro e autenticação locais na mesma sessão, com entradas manuais."""
from pathlib import Path

from config import RAIZ
from erros import ErroBiometria, ErroInterno
from sistema import SistemaBiometrico


def resolver_caminho_imagem(caminho):
    caminho = caminho.strip()
    if not caminho:
        return caminho

    # Aceita caminhos copiados do explorador com aspas externas.
    if len(caminho) >= 2 and caminho[0] == caminho[-1] and caminho[0] in ('"', "'"):
        caminho = caminho[1:-1].strip()

    caminho_path = Path(caminho).expanduser()
    if caminho_path.is_absolute():
        return str(caminho_path)

    candidatos = [
        RAIZ / caminho_path,
        RAIZ / 'imagens' / caminho_path,
    ]

    for candidato in candidatos:
        if candidato.exists():
            return str(candidato)

    # Se o usuário digitar apenas o nome do arquivo, assume que ele está em imagens/.
    return str(RAIZ / 'imagens' / caminho_path)


def executar():
    sistema = SistemaBiometrico()
    operacoes = {
        '1': sistema.cadastrar_pessoa,
        '2': sistema.autenticar_pessoa,
    }
    print('Os cadastros ficam em memória somente enquanto este programa estiver aberto.')
    while True:
        print('\n1 - Cadastrar\n2 - Autenticar\n0 - Sair')
        escolha = input('Operação: ').strip()
        if escolha == '0':
            return
        if escolha not in operacoes:
            print('Opção inválida. Escolha 1, 2 ou 0.')
            continue
        identificador = input('Identificador/nome da pessoa: ').strip()
        caminho = input('Caminho da imagem (qualquer nome de arquivo): ').strip()
        caminho = resolver_caminho_imagem(caminho)
        try:
            resultado = operacoes[escolha](identificador, caminho)
            print(f'{resultado.mensagem} Identificador: {resultado.identificador}')
            if resultado.score is not None:
                print(f'Score Cosine: {resultado.score:.6f}')
            print(f'Etapas: {resultado.etapas}')
        except ErroInterno:
            print('Não foi possível processar a imagem. Tente outra foto. Se persistir, confira a instalação no README.')
        except ErroBiometria as erro:
            print(f'Erro: {erro}')


if __name__ == '__main__':
    try:
        executar()
    except (EOFError, KeyboardInterrupt):
        print('\nSessão encerrada. Os cadastros em memória foram descartados.')
    except ErroInterno:
        raise SystemExit('Não foi possível iniciar o programa. Confira os passos de instalação e download dos modelos no README.')
    except ErroBiometria as erro:
        raise SystemExit(f'Erro: {erro}')
