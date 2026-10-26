# LESSONS - auto-maintained by scripts/lessons.py

> Machine-owned. Do NOT hand-edit. Changes are overwritten on the next `lessons.py` write.
> Canonical state lives in `.specs/lessons.json`. Edit lessons only via the script.
> promote_threshold=2 distinct features · window_days=45 · quarantine_threshold=2

## Confirmed (load these at Specify/Design)

Corroborated across multiple features. Safe to apply as guidance.

_none_

## Candidates (under observation - do NOT load as guidance yet)

Seen once or not yet corroborated. Tracked, not trusted.

### L-001 - Testar cada filtro de consulta no nivel do endpoint com valores esperados que mudam quando o filtro e removido, nao so na camada de consulta
- signal: `surviving_mutant` · recurrence: 1 feature(s) · scope: `api-endpoints` · harmful: 0
- features: painel-vendas
- evidence: validation.md rodada 1 mutante A9 (DASH-04, PainelEndpoints.cs:53) (api-endpoints)
- last seen: 2026-10-26T09:05:54Z

### L-002 - Testar na pagina a ligacao de cada input condicional dos componentes filhos, nao so o componente isolado
- signal: `surviving_mutant` · recurrence: 1 feature(s) · scope: `web-pages` · harmful: 0
- features: painel-vendas
- evidence: validation.md rodada 1 mutante W5 (RNK-05, painel.page.ts:155) (web-pages)
- last seen: 2026-10-26T09:05:55Z

### L-003 - Fixar na spec a base de cada percentual e o criterio de desempate de cada ordenacao com corte
- signal: `spec_precision_gap` · recurrence: 1 feature(s) · scope: `spec` · harmful: 0
- features: painel-vendas
- evidence: DFE-01 e RNK-04 (validation.md rodada 1 gaps 4 e 5) (spec)
- last seen: 2026-10-26T09:05:55Z

### L-004 - Escrever requisitos de compatibilidade de navegador como os motores e versoes que o e2e executa
- signal: `spec_precision_gap` · recurrence: 1 feature(s) · scope: `spec` · harmful: 0
- features: painel-vendas
- evidence: PWA-06 (validation.md rodada 2 gap 1) (spec)
- last seen: 2026-10-26T09:05:55Z

## Quarantined (failed when applied - ignore)

A confirmed lesson that recurred alongside failure. Kept for the maintainer to review.

_none_
