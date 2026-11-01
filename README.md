# interviewer-copilot

> Windows desktop copilot for technical interviews: it listens to the interviewer through the computer's **audio output** (WASAPI loopback, never the microphone), transcribes locally with faster-whisper and streams structured, honest answer suggestions from Claude.

## O que é

Um app desktop para Windows que ajuda em entrevistas técnicas por vídeo e em simulações de entrevista:

1. **Ouve o entrevistador pela saída de áudio do Windows** (alto-falante ou fone Bluetooth) usando WASAPI *loopback*. O app **nunca** abre um microfone.
2. **Transcreve localmente** com faster-whisper (modelos em inglês). O áudio nunca sai do computador.
3. **Detecta quando uma pergunta termina** e gera, em streaming, uma resposta estruturada com o Claude (API da Anthropic):
   - `QUESTION`: a pergunta detectada;
   - `INTENT`: o que o entrevistador quer avaliar;
   - `SUGGESTED ANSWER`: resposta curta e natural, em inglês;
   - `KEY CONCEPTS`: os tópicos técnicos envolvidos;
   - `LEARNING NOTES`: explicação rápida em português.

As respostas seguem o perfil real do candidato e **nunca inventam senioridade nem experiência em produção**.

## Como funciona

```
Áudio que SAI do Windows (alto-falante / fone Bluetooth)
        │  WASAPI loopback (só dispositivos de SAÍDA)
        ▼
Motor de captura em Rust ──► medidor de nível → mono → 16 kHz / 16 bits
        │
        ▼
VAD + faster-whisper (local) ──► transcrição + detecção de pergunta
        │
        ▼
Claude (streaming) ──► cartão com a resposta sugerida na interface React
```

## Regras do projeto

1. **Só loopback.** O áudio vem apenas de um dispositivo de saída. Nada de microfone, nem "Stereo Mix".
2. **Sem fallback.** Se a captura falhar, o app mostra um erro explícito e nunca troca para um microfone.
3. **Fases com validação.** Cada fase só começa depois que a anterior foi comprovada e aprovada.

## Status

| Fase | Situação |
|---|---|
| 1. Diagnóstico de áudio | ✅ Concluída no notebook (loopback comprovado). Repetir no h0wzy-pc |
| Arquitetura v2 | ✅ Pesquisada e revisada (32 achados corrigidos). ⏳ Aguardando aprovação |
| 2. App Tauri + motor de captura + seletor de saída + medidor | ⏳ Não iniciada |
| 3. Transcrição local (faster-whisper) | ⏳ Não iniciada |
| 4. Respostas com Claude + interface completa | ⏳ Não iniciada |

Detalhes e próximos passos: [`docs/SPEC.md`](docs/SPEC.md).

## Stack

- **Desktop:** Tauri 2 (Rust), crate `wasapi` para o WASAPI loopback, `rubato` para reamostragem.
- **Interface:** React, TypeScript, Vite, Tailwind CSS v4, shadcn/ui, lucide.
- **Transcrição:** faster-whisper (`base.en` int8 na CPU por padrão; CUDA opcional no PC com RTX).
- **Respostas:** API Messages da Anthropic com streaming. A chave fica no Gerenciador de Credenciais do Windows.

## Documentação

| Arquivo | Para quê |
|---|---|
| [`docs/SPEC.md`](docs/SPEC.md) | **Comece aqui.** Spec (SDD): requisitos, critérios de aceite, plano de tarefas, status, decisões e pendências |
| [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) | Arquitetura técnica detalhada (motor de captura, IPC, erros, testes, ambiente) |
| [`docs/BRIEF.md`](docs/BRIEF.md) | Briefing original do projeto |
| [`docs/findings/`](docs/findings/README.md) | Medições, pesquisas e a revisão adversarial da arquitetura |
| [`CLAUDE.md`](CLAUDE.md) | Instruções para o agente (Claude Code) continuar o projeto |

## Estrutura

```
docs/               spec, arquitetura, briefing, achados e evidências
tools/diagnostics/  diagnóstico de áudio (somente leitura, nunca abre microfone)
src/                interface React (a partir da Fase 2)
src-tauri/          núcleo em Rust (a partir da Fase 2)
stt-worker/         worker Python do faster-whisper (Fase 3)
```

## Diagnóstico de áudio

Lista os dispositivos de saída e testa o loopback em cada saída ativa. Deixe algum áudio tocando enquanto roda:

```powershell
powershell -ExecutionPolicy Bypass -File tools\diagnostics\audio-probe.ps1 -Seconds 3
```

## Continuar o desenvolvimento

1. Clone o repositório num SSD, fora de pastas sincronizadas com o OneDrive: `git clone https://github.com/H0wZy/interviewer-copilot.git`.
2. Abra o Claude Code na pasta. O [`CLAUDE.md`](CLAUDE.md) e a [`docs/SPEC.md`](docs/SPEC.md) dizem ao agente por onde seguir. O primeiro passo é a Fase 0: diagnóstico da máquina nova, sem instalar nada.
3. As instalações (Visual Studio Build Tools, Rust, Node) só acontecem com a sua autorização.

## Privacidade e uso responsável

- O áudio é processado só na máquina. Para a Anthropic vai apenas texto: a transcrição e o perfil do candidato.
- Gravações de diagnóstico (WAV) ficam fora do repositório e são apagadas automaticamente.
- Muitos processos seletivos proíbem ou monitoram ajuda de IA durante a entrevista, e transcrever uma chamada pode exigir o consentimento da outra parte. Confira as regras de cada processo. O app também serve para treinar com entrevistas simuladas.
