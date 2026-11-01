# Documentation

Reading order for anyone (human or agent) picking up the project:

1. [`SPEC.md`](SPEC.md): **start here.** Spec-driven master document: scope, hard rules, machines, functional and non-functional requirements with acceptance criteria, the ordered task plan with checkboxes, verification gates, decision log, pending decisions and how we work.
2. [`ARCHITECTURE.md`](ARCHITECTURE.md): the technical design (v2, post-review) that the tasks implement: audio engine, no-mic guarantee, DSP, IPC contract, error taxonomy, Phase 2 gate criteria, Phase 3/4 seams, dev environment, risks.
3. [`BRIEF.md`](BRIEF.md): the original project brief, kept verbatim as the historical source of intent. Its Phase 1 registry command has a wrong property GUID; the fixed diagnostics are in `tools/diagnostics/`.
4. [`findings/`](findings/README.md): what was measured and researched, and how the architecture was reviewed.

| Path | Kind | Notes |
|---|---|---|
| `SPEC.md` | spec (living) | update status, checkboxes and the decision log as work progresses |
| `ARCHITECTURE.md` | design (living) | proposal v2 until Marcos approves it (SPEC P-1) |
| `BRIEF.md` | source (frozen) | do not edit |
| `findings/phase1-diagnostics-*.md` | measurements (per machine) | the laptop report exists; h0wzy-pc's is written in Phase 0 |
| `findings/research-*.md` | research (frozen, 2026-11-01) | inputs to the architecture; the architecture wins where they differ |
| `findings/architecture-review.md` | review (frozen) | 32 adversarial findings and how each was resolved |
| `findings/evidence/` | raw evidence | probe sources, traces and benchmark scripts behind the numbers |
| `phase2-gate.md`, `phase3-gate.md`, `phase4-gate.md` | gate evidence | created at each gate (Phase 2: step S7); holds Marcos's dated written confirmation |
