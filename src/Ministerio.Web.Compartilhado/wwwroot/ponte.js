// Ponte entre o Blazor e o navegador: armazenamento local e webcam.

export function guardar(chave, valor) {
    try { localStorage.setItem(chave, valor); } catch { /* modo privado ou bloqueado: a sessão só dura até fechar a aba */ }
}

export function ler(chave) {
    try { return localStorage.getItem(chave); } catch { return null; }
}

export function remover(chave) {
    try { localStorage.removeItem(chave); } catch { /* idem */ }
}

export async function iniciarCamera(video) {
    const fluxo = await navigator.mediaDevices.getUserMedia({
        video: { facingMode: 'user', width: { ideal: 640 }, height: { ideal: 480 } },
        audio: false,
    });
    video.srcObject = fluxo;
    await video.play();
}

// JPEG de no máximo 640 px de largura: bem abaixo do teto de 2 MB da API.
export function capturar(video) {
    if (!video.videoWidth) { return Promise.resolve(null); }
    const escala = Math.min(1, 640 / video.videoWidth);
    const tela = document.createElement('canvas');
    tela.width = Math.round(video.videoWidth * escala);
    tela.height = Math.round(video.videoHeight * escala);
    tela.getContext('2d').drawImage(video, 0, 0, tela.width, tela.height);
    return new Promise(resolver => tela.toBlob(async blob => resolver(blob ? new Uint8Array(await blob.arrayBuffer()) : null), 'image/jpeg', 0.9));
}

export function pararCamera(video) {
    video.srcObject?.getTracks().forEach(faixa => faixa.stop());
    video.srcObject = null;
}

// Baixa um arquivo gerado na memória (o relatório em CSV), sem abrir outra aba: o pedido precisa do token da sessão.
export function baixar(nome, bytes, tipo) {
    const url = URL.createObjectURL(new Blob([bytes], { type: tipo }));
    const link = document.createElement('a');
    link.href = url;
    link.download = nome;
    link.click();
    URL.revokeObjectURL(url);
}
