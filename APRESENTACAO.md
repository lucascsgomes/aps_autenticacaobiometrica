# Roteiro da apresentação: identificação e autenticação biométrica facial

Caso de uso do enunciado: restringir o acesso a uma base de dados do Ministério do Meio Ambiente (propriedades rurais com agrotóxicos proibidos), com três níveis de informação. Tempo sugerido: 15 a 20 minutos, mais perguntas. O foco é o reconhecimento facial (blocos 2 a 5); o resto é o contexto onde ele é usado.

## 0. A pergunta que o professor vai fazer: "isso não é uma biblioteca pronta?"

Responda antes que perguntem, é mais forte. A resposta honesta:

- **O que é pronto:** a biblioteca OpenCV e os dois modelos de rede neural que ela executa. Os modelos são pré-treinados e vêm do repositório OpenCV Zoo: **YuNet** (detecta rostos) e **SFace** (transforma um rosto em 128 números). Nós **não treinamos** rede neural.
- **O que não existe pronto:** uma função "autentique esta pessoa" nem "descubra quem é". Entre a foto e a decisão há um pipeline de processamento de imagem, regras de decisão, limiares por nível, a identificação 1 para N, o serviço e toda a segurança em volta. Isso é o nosso trabalho.
- **Como dizer:** "Usamos dois modelos pré-treinados, como se faz na indústria. Nosso trabalho foi montar o pipeline, decidir quando aceitar ou recusar, medir, e integrar com segurança num controle de acesso por níveis."

Não diga que "desenvolvemos o reconhecimento facial" sem essa distinção.

## 1. O que o enunciado pede e onde está

| Pedido do enunciado | Onde está |
|---|---|
| Ferramenta de identificação e autenticação biométrica em Python | `face-service/` (Python): menu de terminal `cli.py` e serviço HTTP |
| Objeto biométrico escolhido pelo grupo | o rosto |
| Nível 1: todos; nível 2: diretores de divisão; nível 3: só o ministro | `/dados/1`, `/dados/2`, `/dados/3` na API e na interface |
| Imagem por arquivo, câmera ou scanner | câmera na interface; arquivos de imagem no menu de terminal |
| Refinamento, tratamento de erros, extras e relatórios | recusas com motivo, falha fechada, bloqueio por falhas, relatório de segurança com CSV, troca e exclusão do rosto, testes |

## 2. Como o reconhecimento funciona (6 a 8 min, o centro da apresentação)

### 2.1 Autenticação (1 para 1) e identificação (1 para N)

| Operação | Pergunta | Onde |
|---|---|---|
| **Cadastro** | "esta é a minha foto" | a foto vira um vetor de 128 números (o "template"), guardado cifrado; a foto é descartada |
| **Autenticação 1 para 1** | "eu sou a Ana, confirme" | compara a foto com o vetor de uma pessoa só (CPF e rosto) |
| **Identificação 1 para N** | "quem é esta pessoa?" | compara a foto com **todos** os cadastrados e escolhe o melhor (entrar só com o rosto) |

Na identificação, o sistema recusa em dois casos: ninguém passa do limiar, ou o segundo melhor fica a menos de **0,05** do primeiro (duas pessoas parecidas demais). Preferir recusar a escolher errado é uma decisão de projeto.

### 2.2 O caminho da foto, passo a passo

Arquivo central: `face-service/biometria/pipeline.py`, função `_extrair`. Mostre o código na tela.

1. **Entrada** (`app/imagem.py`): a foto chega com até 2 MB, é decodificada pelo OpenCV (não confiamos na extensão) e **reduzida para no máximo 640 px de largura**.
2. **Tons de cinza** (`cv2.cvtColor`).
3. **Pré-processamento com CLAHE** (`processamento/pre_processamento.py`): equalização adaptativa de contraste por regiões (8×8, limite 2,0), que ajuda quando a luz é desigual. O código tem outras opções (equalização simples, gaussiano) e três tratamentos de ruído (mediana, abertura e fechamento morfológico), selecionáveis em `config.py`; o de ruído está desligado. **Não medimos** se o CLAHE melhora o resultado.
4. **Detecção do rosto com YuNet** (`biometria/detector.py`): devolve a posição de cada rosto e 5 pontos (olhos, nariz, cantos da boca), com confiança acima de 0,6.
5. **Regra de um rosto só:** nenhum rosto vira `NO_FACE_DETECTED`; mais de um vira `MULTIPLE_FACES`.
6. **Alinhamento** (`alignCrop` do SFace, em `biometria/reconhecimento.py`): usa os 5 pontos para girar e recortar o rosto num quadrado padrão (112×112), com os olhos sempre no mesmo lugar.
7. **Extração com SFace** (`feature`): a rede transforma o rosto alinhado em **128 números**.
8. **Comparação** (`biometria/comparacao.py`): **similaridade do cosseno** entre os dois vetores, de -1 a 1; mais perto de 1, mais parecidos.
9. **Decisão:** `score >= limiar`. Na identificação, `biometria/identificacao.py` compara com todos, ordena e aplica o limiar e a margem.

### 2.3 O que são esses 128 números

- A rede foi treinada com milhões de rostos para uma regra só: **fotos da mesma pessoa caem perto; pessoas diferentes, longe**, num espaço de 128 dimensões.
- O vetor não é uma foto, mas **é dado biométrico sensível** (LGPD). Por isso é cifrado.
- Analogia: um endereço num mapa de 128 dimensões. O cosseno diz se dois endereços apontam para a mesma direção.

### 2.4 Os limiares por nível e os números medidos

| Decisão | Limiar |
|---|---|
| entrar (login) | **0,363**, valor recomendado pelo OpenCV para o SFace |
| abrir o nível 2 | **0,400** |
| abrir o nível 3, trocar rosto ou senha | **0,500** |

Quanto mais valiosa a informação, mais rígido o limiar. Medimos com fotos públicas reais (o serviço de verdade):

| Comparação | Score |
|---|---|
| mesma foto | 1,000 |
| mesma pessoa, outra foto | 0,668 |
| mesma pessoa, foto menor | 0,947 |
| outra pessoa | 0,041 e **0,269** |

- **Subir o limiar** recusa mais impostores (menos falsa aceitação) e também recusa mais gente legítima (mais falsa rejeição). Não existe valor perfeito: é uma escolha de risco.
- O 0,269 de uma pessoa diferente mostra que a folga até 0,363 é pequena. Com muitos cadastrados, o risco de confundir duas pessoas cresce, e é o argumento para a margem e para o limiar do nível 3.
- **Seja honesto:** amostra pequena, fotos boas, sem calibração com uma população.

## 3. Quando não funciona, e por quê (3 min)

| Situação | O que o sistema faz | Por quê |
|---|---|---|
| Sem rosto, de costas, escuro ou borrado | `NO_FACE_DETECTED`; tira outra foto sem perder o desafio | o YuNet não acha o rosto com confiança |
| Duas pessoas no quadro | `MULTIPLE_FACES` | a regra é exatamente um rosto |
| Contraluz, sombra forte | score mais baixo, pode recusar quem é você | a imagem perde detalhe; o CLAHE ajuda, não resolve tudo |
| Óculos escuros, máscara, boné escondendo o rosto | score baixo ou sem detecção | a rede usa partes do rosto que ficam invisíveis |
| Anos depois, barba, penteado, idade | pode cair o score | o vetor guardado ficou diferente do rosto atual; solução: trocar o rosto em `/perfil` |
| **Duas contas com o mesmo rosto** | a identificação sem CPF recusa as duas ("parecidas demais") | a margem de 0,05 impede escolher; o CPF com rosto continua valendo |
| **Gêmeos ou sósias** | podem passar | o modelo mede parecença, não identidade |
| **Foto de uma foto ou vídeo do rosto** | **pode passar** | **não há detecção de vivacidade**: a principal limitação |
| Serviço de reconhecimento fora do ar | `503`, e **nada é aceito** | falha fechada: sem verificar, não entra |
| Rosto errado | `401`; conta no bloqueio | 5 falhas seguidas bloqueiam por 1 minuto, dobrando até 15 |
| Pedir um nível acima do seu | `403 NIVEL_INSUFICIENTE`, antes de gastar o rosto | o nível fica no banco e só o admin muda; fica no relatório |

Frase para fixar: **"Quando o sistema tem dúvida, ele recusa."**

Diga também (tema geral, não medimos): modelos de reconhecimento facial podem funcionar de modo diferente entre grupos de pessoas, conforme os dados de treino.

## 4. A segurança em volta da biometria (3 min)

- **A foto nunca é guardada.** Só o vetor de 128 números sobrevive, **cifrado** com AES-256-GCM, com o id do usuário como dado associado: copiar o vetor de uma pessoa para a linha de outra **não decifra**. A chave fica fora do banco.
- **O score nunca vai para o cliente** (serviria de oráculo), e o relatório também não o guarda.
- **Cada abertura de nível restrito pede o rosto de novo**, com desafio de uso único (90 s) amarrado à pessoa e ao nível. **A senha não abre nível 2 nem 3.**
- **Ninguém vira ministro sozinho:** o nível fica no banco e só um administrador muda, no painel `/admin`, confirmando com o próprio rosto. Cada mudança entra no relatório.
- **Limiar mais alto onde há mais a proteger** e **falha fechada** quando o serviço cai.
- **Rate limit** e **bloqueio progressivo** contra tentativa em massa.
- **LGPD:** consentimento registrado com a versão do termo; a pessoa pode trocar ou apagar o próprio rosto.

## 5. Demonstração ao vivo (4 min)

Antes: um administrador (CPF em `Admin:Cpfs`) e três cadastrados; promova um a ministro e outro a diretor pelo painel `/admin`. Abra o site 2 minutos antes (os serviços dormem).

1. **Entrar só com o rosto, sem CPF** (identificação 1 para N): "Entrar com o rosto". Mostre a mira na câmera e a animação "Analisando o rosto..." (é o aviso de que o sistema está identificando). Depois, o painel com o nível e o cargo.
2. **Nível 1** como servidor: abre direto, só município, estado, produto e ano.
3. **Nível 2** como diretor: pede o rosto de novo, abre, e a tabela ganha propriedade, quantidade e área.
4. **O momento mais convincente:** peça para o **servidor** tentar o nível 3. Mostre a recusa por nível (`NIVEL_INSUFICIENTE`) e, depois, no relatório, a tentativa registrada.
5. **Rosto de outra pessoa** no nível 2: recusado, e a pessoa precisa de um novo desafio.
6. **Nível 3** como ministro: aparecem proprietário, multa e situação.
7. **Relatório** (só o ministro): gráficos de acessos por tipo e por motivo. Clique num motivo de recusa (por exemplo "Rosto não confirmado") para ver **quem tentou, quando e de qual IP**; quem não foi identificado aparece como "Não identificado" (o CPF digitado não é guardado, por LGPD). O CSV traz tudo.
7b. **Painel admin** (`/admin`): mude o nível de alguém e confirme com o rosto; a mudança aparece no relatório com o antes e o depois.
8. **Plano B** se a câmera ou o serviço falharem: entrar com CPF e senha (nível 1) e mostrar os prints ou o vídeo gravado antes.

## 6. Um bug que a medição achou (1 min)

Ao testar o serviço com várias fotos ao mesmo tempo, **17 de 40 fotos boas** voltavam como "sem rosto" ou "vários rostos". O detector do OpenCV guarda estado interno e não aguenta duas fotos simultâneas. Corrigimos com uma trava (`_TRAVA_DOS_MODELOS` em `pipeline.py`) e criamos um teste que falha sem ela. Depois, 40 de 40 certas. A lição: **testar com carga, não só uma foto por vez**. No teste de ponta a ponta com os modelos reais, outro achado: duas contas com o mesmo rosto viram "parecidas demais" na identificação.

## 7. Limitações e próximos passos (1 min)

- **Sem detecção de vivacidade:** uma foto de uma foto pode ser aceita. Próximo passo: pedir um movimento ou usar um modelo de anti-spoofing.
- **Limiares sem calibração** com um grupo: o próximo passo é medir falsa aceitação e falsa rejeição com consentimento.
- **Cadastro duplicado não é impedido:** poderia recusar um rosto que já existe, com um limiar mais alto para evitar falso bloqueio.
- Pré-processamento sem comparação sistemática; serviços gratuitos que dormem; dados fictícios; liveness não implementado.

## 8. Perguntas prováveis

- **"Vocês treinaram o modelo?"** Não. YuNet e SFace pré-treinados do OpenCV Zoo. O nosso trabalho foi o pipeline, as decisões, a medição e a integração segura.
- **"Qual a diferença entre identificar e autenticar?"** Autenticar é 1 para 1: a pessoa diz quem é e confirmamos. Identificar é 1 para N: ela não diz, e descobrimos entre todos os cadastrados.
- **"Por que 0,363, 0,400 e 0,500?"** 0,363 é o recomendado pelo OpenCV para o SFace. Subimos para os níveis restritos porque o que se protege vale mais. Não calibramos com uma população.
- **"E se a pessoa mostrar uma foto?"** Pode passar; não há vivacidade. A mitigação hoje é o limiar mais alto, o limite de tentativas, o bloqueio e o registro no relatório.
- **"Onde fica o rosto da pessoa?"** Só um vetor de 128 números, cifrado, no banco. A foto não é gravada em lugar nenhum.
- **"Como o sistema sabe quem é ministro?"** Fica no banco, e só um administrador muda, no painel, confirmando com o rosto. Ninguém se promove sozinho.
- **"Por que a senha não abre o nível 3?"** Para a senha não virar a porta dos fundos da biometria: nos níveis restritos só o rosto vale.
- **"E se o banco vazar?"** O vetor está cifrado com chave fora do banco, e cada vetor só decifra para o seu dono.
- **"Quanto tempo leva?"** Cerca de 0,4 s por foto no computador de desenvolvimento; o plano gratuito da nuvem é mais lento.
- **"E se o serviço de reconhecimento cair?"** Nenhum rosto é aceito sem verificar.

## 9. Checklist antes de apresentar

- [ ] **2 minutos antes:** abra `/health` da API e `/internal/v1/health` do `face-service` e espere responderem (eles dormem).
- [ ] No servidor, `Admin__Cpfs__0` com o CPF do administrador; cadastre antes o administrador e mais dois, cada um com o seu rosto, e promova pelo `/admin`.
- [ ] Teste a câmera do computador que vai usar e a permissão do navegador.
- [ ] Combine com alguém (com consentimento) para o teste de rosto de outra pessoa.
- [ ] Grave um vídeo curto da demonstração funcionando, como plano B.
- [ ] Tenha o código aberto: `pipeline.py` (`_extrair`), `detector.py`, `reconhecimento.py`, `comparacao.py`, `identificacao.py`.
- [ ] Combine quem apresenta cada parte: o serviço facial em Python e a integração, a API e a interface.
