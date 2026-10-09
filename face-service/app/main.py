"""Microserviço de reconhecimento facial. Stateless: não acessa banco e não grava imagem."""
import uuid
from contextlib import asynccontextmanager

from fastapi import Depends, FastAPI, File, Form, Request, UploadFile
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse

from app import embedding
from app.esquemas import EnrollResposta, ProntoResposta, VerifyResposta, VivoResposta
from app.imagem import decodificar_imagem
from app.seguranca import NaoAutorizado, exigir_chave_interna
from biometria.comparacao import THRESHOLD_COSINE, comparar, corresponde
from biometria.pipeline import PipelineFacial
from config import LIMIAR_MAXIMO, LIMIAR_MINIMO, LIMITE_IMAGEM_BYTES
from erros import ErroBiometria

STATUS_POR_CODIGO = {
    'INVALID_IMAGE': 400,
    'INVALID_REFERENCE': 400,
    'INVALID_THRESHOLD': 400,
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
def enroll(request: Request, image: UploadFile = File(...), pipeline: PipelineFacial = Depends(obter_pipeline)):
    imagem = decodificar_imagem(_ler_limitado(image))
    vetor = pipeline.extrair_de_imagem(imagem)
    return EnrollResposta(embedding_b64=embedding.codificar(vetor), dim=embedding.DIM,
                          request_id=request.state.request_id)


@app.post('/internal/v1/face/verify', response_model=VerifyResposta, dependencies=[Depends(exigir_chave_interna)])
def verify(request: Request, image: UploadFile = File(...), reference_b64: str = Form(...),
           threshold: float | None = Form(default=None),
           pipeline: PipelineFacial = Depends(obter_pipeline)):
    limiar = THRESHOLD_COSINE if threshold is None else threshold
    if not LIMIAR_MINIMO <= limiar <= LIMIAR_MAXIMO:
        raise ErroBiometria('Limiar fora da faixa permitida (0,300 a 0,900).', codigo='INVALID_THRESHOLD')
    referencia = embedding.decodificar(reference_b64)
    imagem = decodificar_imagem(_ler_limitado(image))
    vetor = pipeline.extrair_de_imagem(imagem)
    score = comparar(pipeline.reconhecedor.modelo, referencia, vetor)
    return VerifyResposta(match=corresponde(score, limiar), score=round(score, 6), threshold=limiar,
                          request_id=request.state.request_id)


def _ler_limitado(arquivo: UploadFile) -> bytes:
    """Lê no máximo limite+1 bytes, para recusar sem carregar um upload gigante inteiro na memória."""
    return arquivo.file.read(LIMITE_IMAGEM_BYTES + 1)
