from pydantic import BaseModel

MODELO = 'sface-2021dec'


class EnrollResposta(BaseModel):
    embedding_b64: str
    dim: int
    model: str = MODELO
    faces_detected: int = 1
    request_id: str


class VerifyResposta(BaseModel):
    match: bool
    score: float
    threshold: float
    metric: str = 'cosine'
    faces_detected: int = 1
    model: str = MODELO
    request_id: str


class VivoResposta(BaseModel):
    status: str = 'vivo'


class ProntoResposta(BaseModel):
    yunet: str
    sface: str
    model: str = MODELO
