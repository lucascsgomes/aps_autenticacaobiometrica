FROM python:3.12-slim

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    PORT=7860

WORKDIR /app

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

# Modelos baixados no build (não ficam no repositório). O build falha se o download falhar.
ADD --chmod=644 https://media.githubusercontent.com/media/opencv/opencv_zoo/main/models/face_detection_yunet/face_detection_yunet_2023mar.onnx modelos/
ADD --chmod=644 https://media.githubusercontent.com/media/opencv/opencv_zoo/main/models/face_recognition_sface/face_recognition_sface_2021dec.onnx modelos/

COPY app app
COPY cadastro cadastro
COPY biometria biometria
COPY processamento processamento
COPY visualizacao visualizacao
COPY config.py erros.py ./

# Alguns construtores (o do Render) criam modelos/ só para o root; o usuário sem privilégio precisa ler tudo em /app.
RUN chmod -R a+rX /app && useradd --create-home --uid 10001 servico
USER servico

EXPOSE 7860
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s \
    CMD python -c "import os,urllib.request; urllib.request.urlopen(f'http://127.0.0.1:{os.environ[\"PORT\"]}/internal/v1/health')"

# FACE_INTERNAL_KEY vem do ambiente do host; nunca é gravada na imagem.
CMD ["sh", "-c", "exec uvicorn app.main:app --host 0.0.0.0 --port ${PORT}"]
