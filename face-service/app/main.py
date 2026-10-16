"""Microserviço de reconhecimento facial. Stateless: não acessa base de dados e não grava imagem."""
import json
import uuid
from contextlib import asynccontextmanager

from fastapi import Depends, FastAPI, File, Form, Request, UploadFile
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse

from app import embedding
from app.esquemas import EnrollResposta, IdentifyResposta, ProntoResposta, VerifyResposta, VivoResposta
from app.imagem import decodificar_imagem
from app.seguranca import NaoAutorizado, exigir_chave_interna
from biometria.comparacao import THRESHOLD_COSINE, comparar, corresponde
from biometria.identificacao import MARGEM_PADRAO, identificar
from biometria.pipeline import PipelineFacial
from config import (
    LIMIAR_MAXIMO,
    LIMIAR_MINIMO,
    LIMITE_CANDIDATOS,
    LIMITE_IMAGEM_BYTES,
    MARGEM_MAXIMA,
    ConfiguracaoProcessamento,
)
from erros import ErroBiometria

STATUS_POR_CODIGO = {
    'INVALID_IMAGE': 400,
    'INVALID_REFERENCE': 400,
    'INVALID_THRESHOLD': 400,
    'INVALID_MARGIN': 400,
    'INVALID_CANDIDATES': 400,
    'INVALID_PROCESSING': 400,
    'IMAGE_TOO_LARGE': 413,
    'NO_FACE_DETECTED': 422,
    'MULTIPLE_FACES': 422,
}


def problema(status: int, codigo: str, detalhe: str, request_id: str):
    return JSONResponse(
        status_code=status,
        media_type='application/problem+json',
        headers={'X-Request-Id': request_id},
        content={'type': f'https://httpstatuses.com/{status}', 'title': codigo, 'status': status,
                 'code': codigo, 'detail': detalhe, 'request_id': request_id},
    )


@asynccontextmanager
async def ciclo_de_vida(app: FastAPI):
    app.state.pipeline = None
    try:
        app.state.pipeline = PipelineFacial()
    except ErroBiometria:  # modelos ausentes: o serviço sobe, mas /ready devolve 503
        pass
    yield


def _configuracao(preprocessing: str | None, noise: str | None):
    """Escolha do admin, mandada pela API a cada chamada. Sem nenhuma, vale a configuração do serviço.
    noise='none' desliga o tratamento de ruído."""
    if preprocessing is None and noise is None:
        return None
    base = ConfiguracaoProcessamento()
    escolhida = ConfiguracaoProcessamento(preprocessing or base.pre_processamento,
                                          None if noise == 'none' else (noise or base.tratamento_ruido))
    escolhida.validar()
    return escolhida


def obter_pipeline(request: Request) -> PipelineFacial:
    if request.app.state.pipeline is None:
        raise ErroBiometria('Modelos não carregados.', codigo='MODELS_NOT_LOADED')
    return request.app.state.pipeline


# Sem docs/openapi públicos: o serviço é interno e não deve ter superfície extra.
app = FastAPI(title='face-service', version='1.0.0', lifespan=ciclo_de_vida,
              docs_url=None, redoc_url=None, openapi_url=None)


@app.middleware('http')
async def request_id(request: Request, chamar_proximo):
    request.state.request_id = request.headers.get('x-request-id') or str(uuid.uuid4())
    resposta = await chamar_proximo(request)
    resposta.headers['X-Request-Id'] = request.state.request_id
    return resposta


@app.exception_handler(NaoAutorizado)
async def _nao_autorizado(request: Request, _):
    return problema(401, 'UNAUTHORIZED', 'Chave interna ausente ou inválida.', request.state.request_id)


@app.exception_handler(ErroBiometria)
async def _erro_biometria(request: Request, erro: ErroBiometria):
    if erro.codigo == 'MODELS_NOT_LOADED':
        return problema(503, 'MODELS_NOT_LOADED', 'Modelos não carregados.', request.state.request_id)
    status = STATUS_POR_CODIGO.get(erro.codigo)
    if status is None:  # falha interna: não vaza detalhe técnico
        return problema(500, 'INTERNAL_ERROR', 'Falha interna no processamento facial.', request.state.request_id)
    return problema(status, erro.codigo, str(erro), request.state.request_id)


@app.exception_handler(RequestValidationError)
async def _requisicao_invalida(request: Request, _):
    return problema(400, 'INVALID_REQUEST', 'Campos obrigatórios ausentes ou malformados.', request.state.request_id)


@app.get('/internal/v1/health', response_model=VivoResposta)
def health():
    return VivoResposta()


@app.get('/internal/v1/ready', response_model=ProntoResposta)
def ready(request: Request):
    if request.app.state.pipeline is None:
        raise ErroBiometria('Modelos não carregados.', codigo='MODELS_NOT_LOADED')
    return ProntoResposta(yunet='ok', sface='ok')


# `def` síncrono de propósito: o OpenCV bloqueia, então roda no threadpool do FastAPI.
@app.post('/internal/v1/face/enroll', response_model=EnrollResposta, dependencies=[Depends(exigir_chave_interna)])
def enroll(request: Request, image: UploadFile = File(...), preprocessing: str | None = Form(default=None),
           noise: str | None = Form(default=None), pipeline: PipelineFacial = Depends(obter_pipeline)):
    imagem = decodificar_imagem(_ler_limitado(image))
    vetor = pipeline.extrair_de_imagem(imagem, _configuracao(preprocessing, noise))
    return EnrollResposta(embedding_b64=embedding.codificar(vetor), dim=embedding.DIM,
                          request_id=request.state.request_id)


@app.post('/internal/v1/face/verify', response_model=VerifyResposta, dependencies=[Depends(exigir_chave_interna)])
def verify(request: Request, image: UploadFile = File(...), reference_b64: str = Form(...),
           threshold: float | None = Form(default=None), preprocessing: str | None = Form(default=None),
           noise: str | None = Form(default=None), pipeline: PipelineFacial = Depends(obter_pipeline)):
    limiar = THRESHOLD_COSINE if threshold is None else threshold
    if not LIMIAR_MINIMO <= limiar <= LIMIAR_MAXIMO:
        raise ErroBiometria('Limiar fora da faixa permitida (0,300 a 0,900).', codigo='INVALID_THRESHOLD')
    referencia = embedding.decodificar(reference_b64)
    imagem = decodificar_imagem(_ler_limitado(image))
    vetor = pipeline.extrair_de_imagem(imagem, _configuracao(preprocessing, noise))
    score = comparar(pipeline.reconhecedor.modelo, referencia, vetor)
    return VerifyResposta(match=corresponde(score, limiar), score=round(score, 6), threshold=limiar,
                          request_id=request.state.request_id)


@app.post('/internal/v1/face/identify', response_model=IdentifyResposta, dependencies=[Depends(exigir_chave_interna)])
def identify(request: Request, image: UploadFile = File(...), candidates: str = Form(...),
             threshold: float | None = Form(default=None), margin: float | None = Form(default=None),
             preprocessing: str | None = Form(default=None), noise: str | None = Form(default=None),
             pipeline: PipelineFacial = Depends(obter_pipeline)):
    """1 para N: `candidates` é um JSON `[{"id": ..., "embedding_b64": ...}]`.
    Só devolve o id quando há um vencedor claro."""
    limiar = THRESHOLD_COSINE if threshold is None else threshold
    if not LIMIAR_MINIMO <= limiar <= LIMIAR_MAXIMO:
        raise ErroBiometria('Limiar fora da faixa permitida (0,300 a 0,900).', codigo='INVALID_THRESHOLD')
    folga = MARGEM_PADRAO if margin is None else margin
    if not 0 <= folga <= MARGEM_MAXIMA:
        raise ErroBiometria('Margem fora da faixa permitida (0 a 0,500).', codigo='INVALID_MARGIN')
    lista = _ler_candidatos(candidates)
    imagem = decodificar_imagem(_ler_limitado(image))
    vetor = pipeline.extrair_de_imagem(imagem, _configuracao(preprocessing, noise))
    r = identificar(pipeline.reconhecedor.modelo, lista, vetor, limiar, folga)
    return IdentifyResposta(identified=r.identificado, candidate_id=r.candidato, score=round(r.score, 6),
                            second_score=None if r.segundo_score is None else round(r.segundo_score, 6),
                            ambiguous=r.ambiguo, threshold=limiar, margin=folga, candidates_compared=len(lista),
                            request_id=request.state.request_id)


def _ler_candidatos(texto: str):
    erro = ErroBiometria('Candidatos inválidos: envie um JSON [{"id": "...", "embedding_b64": "..."}].',
                         codigo='INVALID_CANDIDATES')
    try:
        bruto = json.loads(texto)
    except ValueError as causa:
        raise erro from causa
    if not isinstance(bruto, list) or not 1 <= len(bruto) <= LIMITE_CANDIDATOS:
        raise ErroBiometria(f'Envie de 1 a {LIMITE_CANDIDATOS} candidatos.', codigo='INVALID_CANDIDATES')
    candidatos, vistos = [], set()
    for item in bruto:
        if not (isinstance(item, dict) and isinstance(item.get('id'), str)
                and isinstance(item.get('embedding_b64'), str)):
            raise erro
        id_ = item['id']
        if not 1 <= len(id_) <= 64 or id_ in vistos:
            raise ErroBiometria('Cada id deve ter de 1 a 64 caracteres e não pode se repetir.',
                                codigo='INVALID_CANDIDATES')
        vistos.add(id_)
        candidatos.append((id_, embedding.decodificar(item['embedding_b64'])))
    return candidatos


def _ler_limitado(arquivo: UploadFile) -> bytes:
    """Lê no máximo limite+1 bytes, para recusar sem carregar um upload gigante inteiro na memória."""
    return arquivo.file.read(LIMITE_IMAGEM_BYTES + 1)
