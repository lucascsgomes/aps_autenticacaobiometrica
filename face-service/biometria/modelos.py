from pathlib import Path
from erros import ErroInterno


def verificar_modelo(caminho):
    caminho = Path(caminho)
    if not caminho.is_file():
        raise ErroInterno(f"Modelo ausente: {caminho}. Baixe os modelos seguindo os comandos do README.md.")
    try:
        with caminho.open('rb') as arquivo:
            inicio = arquivo.read(100)
        if not inicio or inicio.startswith(b'version https://git-lfs'):
            raise ErroInterno(f"Modelo inválido (vazio ou ponteiro Git LFS): {caminho}")
    except OSError as erro:
        raise ErroInterno(f"Não foi possível ler o modelo: {caminho}") from erro
    return str(caminho)
