# Docker stacks on the main Raspberry Pi

Each folder is a [Dockge](https://github.com/louislam/dockge) stack: a `compose.yaml` plus the files it mounts.

| Stack | What |
|---|---|
| `booth` | RabbitMQ, PostgreSQL, pgAdmin, the RabbitMQ-to-Azure bridge, ServiceControl (primary, audit, monitoring) and the ngrok tunnel to ServiceControl |
| `observability` | OpenTelemetry Collector, Prometheus and Grafana. Joins the `booth` network, so `booth` has to be started first |
| `dozzle` | Container log viewer |

Settings and secrets live in a `.env` next to each `compose.yaml`. Only `.env.example` is committed; the real `.env`
exists only on the Pi.

## Deploying

The repository is the source of truth: change the stacks here, not in the Dockge editor. Dockge is still handy to
watch, restart and read logs.

1. Commit, push and run the **Release** workflow. It publishes this folder as the `stacks` artifact.
2. On the main Pi:

   ```
   ~/download-all.sh            # also downloads the stacks to ~/stacks
   ~/scripts/deploy-stacks.sh   # or: deploy-stacks.sh observability
   ```

`deploy-stacks.sh` copies each stack to `/opt/stacks/<stack>` and runs `docker compose up -d --remove-orphans`, which
recreates only the containers whose configuration changed. There is no need to stop Docker or the stack first. Volumes
(databases, Grafana and Prometheus data) are kept. Files deleted from the repository are not deleted on the Pi.

The first time a stack is deployed, the script creates its `.env` from `.env.example` and stops so you can fill it in.

## Observability

```
 Sales, ClientUI (Pis) ──OTLP :4317──┐
 ServiceControl (Pi) ────────────────┤
                                     ├─► OpenTelemetry Collector ──► Aspire dashboard :18888  (traces, logs, metrics)
 Billing, Shipping,  ──tunnel──:4319─┤        │
 ServiceControl (Azure)   + token    │        └──► Prometheus :9090 ──► Grafana :3000  (big-screen dashboard)
 RabbitMQ, PostgreSQL ◄── scraped ───┘
```

Ports opened on the Pi:

| Port | What |
|---|---|
| 4317 / 4318 | OTLP gRPC / HTTP, no authentication: for the booth network only |
| 4319 | OTLP HTTP requiring `Authorization: Bearer <OTLP_PUBLIC_TOKEN>`: the only one to expose through the tunnel |
| 13133 | Collector health |
| 9090 | Prometheus |
| 3000 | Grafana, anonymous read-only. The **Booth demo** dashboard is the home page; append `?kiosk` for the booth screen |
| 18888 | Aspire dashboard, no login |

### Aspire dashboard

The `aspire-dashboard` container shows the traces, structured logs and metrics the Collector forwards to it, at
`http://<pi>:18888`. It keeps them in memory only.

For the Resources page with health checks of the platform and the booth containers, run `src/BoothDemo.AppHost` on a
laptop on the booth network. The Collector also forwards everything to `LAPTOP_OTLP_ENDPOINT` (the laptop's
`<address>:18889`). Set `BoothPi` in the AppHost's `appsettings.json` to the main Pi's address, allow inbound TCP 18889
in the laptop's firewall, then:

```
dotnet run --launch-profile booth
```

The dashboard is at `http://localhost:18888`. While the laptop is away, the Collector logs failed exports to it.

### Sending telemetry

Endpoints export over OTLP when the standard OpenTelemetry variables are set. On the Pis (Sales, ClientUI):

```
export OTEL_EXPORTER_OTLP_ENDPOINT=http://<main pi>:4317
```

For Azure, the `ngrok` service in the `booth` stack forwards `NGROK_OTLP_URL` to the Collector's port 4319. Pass that
URL and the token to the deployment, which configures Billing, Shipping and ServiceControl:

```
./scripts/deploy.ps1 -ResourceGroupName <rg> -OtlpEndpoint https://<tunnel hostname> -OtlpToken <OTLP_PUBLIC_TOKEN>
```
