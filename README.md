# CorePoints Shared Libraries

Bibliotecas compartilhadas do projeto CorePoints, seguindo os padrões de governança definidos em `architecture-governance/`.

## Estrutura

```
src/
├── CorePoints.Caching/          # Redis Cache-Aside com circuit breaker
├── CorePoints.FeatureToggles/   # Feature flags com PostgreSQL + IMemoryCache
└── CorePoints.Resilience/       # Polly v8 pipelines, typed clients, health checks

tests/
├── CorePoints.Caching.Tests/
└── CorePoints.FeatureToggles.Tests/
```

## Libraries

| Library | Responsabilidade |
|---------|-----------------|
| **CorePoints.Caching** | Cache-Aside pattern com Redis (StackExchange.Redis), invalidação síncrona para Ledger, event-driven para Product, circuit breaker via Polly |
| **CorePoints.FeatureToggles** | Feature flags com PostgreSQL (Dapper), cache IMemoryCache 60s TTL, Admin API, FeatureGate endpoint filter, canary release |
| **CorePoints.Resilience** | Polly v8 pipelines (retry + circuit breaker + bulkhead + timeout), typed LedgerHttpClient, health checks, rate limiting |

## Governança
Este repositório segue as normas definidas em `../architecture-governance/standards/`.
