# MedStorage: Observability та AIOps

Стек спостережуваності для мікросервісів MedStorage (Inventory, Catalog, Alerts, User):

```
 .NET сервіси ──OTLP gRPC :4317──► Jaeger        (трейси)
      │  GET /metrics ◄── scrape 15s ── Prometheus ──alerts──► Alertmanager ──webhook──► n8n ──► тікет + Slack
      │                                    ▲
      └─ stdout: JSON-логи з TraceId/SpanId  └── Grafana (дашборд, Explore → Jaeger)
```

| Компонент    | URL                     | Логін       |
|--------------|-------------------------|-------------|
| Grafana      | http://localhost:3001   | admin/admin |
| Jaeger UI    | http://localhost:16686  | —           |

**Версія Jaeger закріплена на 2.20.0.** У Jaeger 2.21+ прибрано старий HTTP API (`/api/services`, `/api/traces`). Jaeger-datasource у Grafana 13.2 досі працює через нього, тож з новішим Jaeger показує `request failed: 404 Not Found`. Перевірено на 2.18, 2.20, 2.21 і 2.22. Для запитів скриптами використовуйте `/api/v3/...`, він є в усіх v2-версіях.
| Prometheus   | http://localhost:9090   | —           |
| Alertmanager | http://localhost:9093   | —           |
| n8n          | http://localhost:5678   | створюється при першому вході |

Grafana на порту **3001**, бо `localhost:3000` — це origin веб-клієнта в CORS-політиці User Service.

## Структура

```
observability/
├── docker-compose.yml                          # Prometheus, Alertmanager, Jaeger, Grafana, n8n (include з кореневого compose)
├── prometheus/prometheus.yml                   # scrape-конфіг (Docker DNS service discovery)
├── prometheus/alerts.rules.yml                 # HighLatency, ServiceDown, HighErrorRate
├── alertmanager/alertmanager.yml               # маршрут → n8n webhook
├── grafana/provisioning/datasources/           # Prometheus + Jaeger як data sources
├── grafana/provisioning/dashboards/            # провайдер дашбордів
├── grafana/provisioning/{plugins,alerting}/    # порожні; без них Grafana пише помилки при старті
├── grafana/dashboards/medstorage-overview.json # дашборд "MedStorage / Service Overview"
└── n8n/
    ├── workflows/incident-response.json        # n8n export: Webhook → Parse → Ticket (Jira mock) → Slack
    └── sample-alert.json                       # приклад payload від Alertmanager для ручного тесту
```

## 1. Інтеграція в .NET (Завдання 1)

У кожному сервісі є `Infrastructure/Observability/ObservabilityExtensions.cs` (своя копія в кожному сервісі, як і `JwtAuth.cs`, бо Docker build context — папка сервісу). Підключення в `Program.cs`:

```csharp
using MedStorage.Inventory.Api.Infrastructure.Observability;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMedStorageObservability(builder.Configuration, serviceName: "inventory-api");
// ...
var app = builder.Build();
app.UseMedStorageObservability();   // /metrics + Serilog request logging; одразу після Build()
```

Що вмикається:

- **Tracing.** Інструментування ASP.NET Core, HttpClient, EF Core і MassTransit (ActivitySource `MassTransit`), експорт OTLP gRPC у Jaeger. MassTransit передає W3C `traceparent` у заголовках повідомлень, тому один трейс проходить шлях `HTTP → Inventory → RabbitMQ → Alerts consumer`. Запити `/metrics`, `/swagger`, `/health` у трейси не потрапляють.
- **Metrics.** `http.server.request.duration` (latency p50/p95/p99, RPS, error rate), `http.client.request.duration`, runtime-метрики .NET (CPU, working set, GC, thread pool), метрики MassTransit. Віддаються на `GET /metrics` у форматі Prometheus. Самі запити скрейпу виключені з HTTP-метрик (`DisableHttpMetrics`).
- **Logging.** Serilog замінює стандартні провайдери; кожна подія отримує `TraceId`, `SpanId`, `Service`, `Instance`. Одна підсумкова лінія на HTTP-запит (`UseSerilogRequestLogging`).

Конфігурація (`appsettings.json` → секція `Observability`, або змінні середовища):

| Ключ                            | Приклад              | Опис |
|---------------------------------|----------------------|------|
| `Observability__OtlpEndpoint`   | `http://jaeger:4317` | Порожньо → трейси створюються (кореляція логів працює), але не експортуються |
| `Observability__LogFormat`      | `json` / `text`      | `json` у контейнерах, `text` (за замовчуванням) локально |
| `Serilog__MinimumLevel__Override__<Namespace>` | `Debug` | Перевизначення рівнів логування |

В `docker-compose.yml` кожного сервісу вже додано `Observability__OtlpEndpoint=http://jaeger:4317` та `Observability__LogFormat=json`.

Приклад JSON-лога (реальний вивід User API):

```json
{"@t":"2026-11-15T22:50:53.869Z","@m":"HTTP \"GET\" \"/api/v1/user/me\" responded 404 in 41.6351 ms",
 "RequestPath":"/api/v1/user/me","StatusCode":404,"Elapsed":41.6351,
 "TraceId":"cb69b765a11a4b7bc97c0fed74e1bdbc","SpanId":"30e0aff7f56d0268","Service":"user-api","Instance":"..."}
```

`TraceId` можна вставити в пошук Jaeger (поле *Lookup by Trace ID*) і відкрити повний трейс цього запиту.

## 2. Запуск (Завдання 2)

```bash
docker compose up --build -d
```

Перевірка:
- http://localhost:9090/targets: `catalog-api`, `alerts-api`, `user-api` по одному таргету, у `inventory-api` **два** (обидві репліки). Це дає `dns_sd_configs` з типом `A`: Docker DNS повертає IP кожного контейнера.
- Зробіть кілька запитів через Swagger, а потім у Jaeger виберіть сервіс `inventory-api` → *Find Traces*.

## 3. Дашборд і алерти (Завдання 3)

**Grafana** → *Dashboards → MedStorage → MedStorage / Service Overview* (провізіонується автоматично). Фільтр `Service` вгорі.

| Рядок | Панель | PromQL (спрощено) |
|---|---|---|
| Health | Instances up | `sum by (job) (up)` |
| Golden signals | Throughput (RPS) | `sum by (job) (rate(http_server_request_duration_seconds_count[$__rate_interval]))` |
| | Error rate 5xx | `rate(..._count{http_response_status_code=~"5.."}) / rate(..._count)` |
| | Latency p50/p95/p99 | `histogram_quantile(0.99, sum by (job, le) (rate(http_server_request_duration_seconds_bucket[...])))` |
| | p95 by route, Requests by status | ті ж метрики з `http_route` / `http_response_status_code` |
| Resources | CPU (cores) | `rate(dotnet_process_cpu_time_seconds_total[...])` |
| | Memory | `dotnet_process_memory_working_set_bytes` |
| | GC heap, Thread pool | `dotnet_gc_last_collection_heap_size_bytes`, `dotnet_thread_pool_*` |
| | Outgoing HTTP p95 | `http_client_request_duration_seconds_bucket` by `server_address` |

Щоб створити дашборд вручну: *Dashboards → New → Add visualization → Prometheus*, вставити запит з таблиці, обрати unit (`s`, `reqps`, `percentunit`, `bytes`). Або *Import* і завантажити `medstorage-overview.json`.

Ресурси показано **на рівні процесу .NET у кожному контейнері** (по одній лінії на репліку). Метрики на рівні cgroup для всіх контейнерів (включно з Postgres і RabbitMQ) потребують cAdvisor. На Docker Desktop for Windows він працює нестабільно, тому в стек його не додано.

**Правила** (`prometheus/alerts.rules.yml`):

| Alert | Умова | for | severity |
|---|---|---|---|
| `HighLatency` | p99 за 5 хв > 500 ms | 2m | warning |
| `ServiceDown` | `up == 0` (контейнер є, але не відповідає) **або** `absent(up{job=...})` (контейнер зупинено, тож він зник із DNS) | 1m | critical |
| `HighErrorRate` | частка 5xx за 5 хв > 5% | 2m | critical |

Друга форма `ServiceDown` потрібна, бо при DNS discovery зупинений контейнер зникає зі списку таргетів, і `up == 0` не спрацює. Alertmanager гасить `HighLatency`/`HighErrorRate`, поки той самий сервіс `ServiceDown` (inhibit rule).

Після зміни правил: `curl -X POST http://localhost:9090/-/reload`.

## 4. Automated Incident Response у n8n (Завдання 4)

Пайплайн (`n8n/workflows/incident-response.json`):

```
[Alertmanager Webhook] → [Parse Alerts] → [Create/Resolve Incident Ticket (Jira mock)] → [Notify Slack]
 POST /webhook/alertmanager   (Code)              (Code, workflow static data)           (HTTP Request)
```

1. **Alertmanager Webhook**: нода Webhook, `POST`, path `alertmanager`, відповідає одразу (`onReceived`). Alertmanager шле на `http://n8n:5678/webhook/alertmanager` (див. `alertmanager.yml`).
2. **Parse Alerts**: бере `body.alerts[]` і видає один item на алерт з полями `service`, `problemType` (*High Latency* / *High Error Rate* / *Service Down*), `severity`, `status` (firing/resolved), `instance`, `description`, `fingerprint`.
3. **Create/Resolve Incident Ticket (Jira mock)** імітує Jira:
   - `firing` + нового тікета немає → створює `MEDOPS-0001` (priority за severity, labels, посилання на дашборд Grafana з фільтром сервісу і на пошук трейсів у Jaeger);
   - `firing` для вже відкритого → `updated`, `occurrences++` (повторні сповіщення не плодять тікети);
   - `resolved` → закриває **той самий** тікет (ключ: Alertmanager `fingerprint`, збережений у `$getWorkflowStaticData`).
4. **Notify Slack**: POST у Slack Incoming Webhook з `SLACK_WEBHOOK_URL`. Якщо змінна порожня, нода падає м'яко (`continueRegularOutput`), а тікет усе одно видно в *Executions*. Щоб використати Telegram/справжню Jira, замініть цю ноду на *Telegram → Send Message* або *Jira Software → Create Issue* і підставте `{{ $json.ticket.summary }}` / `{{ $json.slackText }}`.

### Налаштування

1. Відкрийте http://localhost:5678 і створіть owner-акаунт (локальний, лише для цього інстансу).
2. Імпорт воркфлоу: *Workflows → ⋯ → Import from File* → `observability/n8n/workflows/incident-response.json`, або через CLI:
   ```bash
   docker compose exec n8n n8n import:workflow --input=/workflows/incident-response.json
   ```
3. **Опублікуйте (активуйте)** воркфлоу: кнопка *Publish* в редакторі, або через CLI:
   ```bash
   docker compose exec n8n n8n publish:workflow --id=MedStorageIncident01
   ```
   Production-URL `/webhook/alertmanager` працює тільки для опублікованого воркфлоу; для неопублікованого є лише тестовий `/webhook-test/alertmanager` після натискання *Execute workflow*. Кроки 2–3 через CLI працюють навіть до створення owner-акаунта.

   > У Git Bash на Windows додайте `MSYS_NO_PATHCONV=1` перед командою, інакше `/workflows/...` перетвориться на Windows-шлях.
4. (Опційно) Slack: створіть Incoming Webhook і перед `docker compose up` задайте змінну:
   ```bash
   export SLACK_WEBHOOK_URL=https://hooks.slack.com/services/XXX/YYY/ZZZ
   ```

### Перевірка

Ручний виклик тим самим payload, який шле Alertmanager:

```bash
curl -X POST http://localhost:5678/webhook/alertmanager -H "Content-Type: application/json" --data @observability/n8n/sample-alert.json
```

У *Executions* має з'явитися тікет `MEDOPS-000N` з `action: "created"`.

Наскрізний тест через справжній алерт:

```bash
docker compose stop alerts.api
```

Приблизно через 1–2 хв (`for: 1m` + `group_wait: 30s`) `ServiceDown` для `alerts-api` з'явиться на http://localhost:9093, а в n8n — новий тікет. Після `docker compose start alerts.api` прийде `resolved` і тікет закриється.

### Розвиток до Agentic AIOps

Між *Parse Alerts* і *Ticket* можна вставити ноду **AI Agent** з tools *HTTP Request*: Prometheus API (`/api/v1/query`) для контексту метрик, Jaeger API (`/api/v3/traces?query.service_name=...`) для повільних і помилкових трейсів. Агент формує гіпотезу першопричини, і вона додається в опис тікета. Безпечну автоматичну дію (наприклад, restart контейнера) варто запускати лише після підтвердження людиною (нода *Wait* + approve-кнопка в Slack).
