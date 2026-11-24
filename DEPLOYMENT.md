# Spyglass Deployment Guide

This project follows a **Hybrid Deployment Strategy** to bypass datacenter IP blocking (like Cloudflare) by routing specific requests through a residential network (**Residential Node**).

---

## 🏗 Architecture Overview
1.  **Remote Server (elitedesk):** Runs the main .NET backend under rootless Podman (`systemd --user` unit `spyglass.service`) and connects to the production MongoDB Atlas.
2.  **Residential Node (Home PC/Mac/Pi):** Runs a custom Node.js relay server and a Cloudflare Tunnel. It provides the residential IP address used for "forbidden" requests.
3.  **Request Flow:** If the Remote Server receives a `403 Forbidden` from a target site, it automatically retries the request through the Residential Node relay via the Cloudflare Tunnel (`proxy-spyglass.cyc.fyi`).

> **Current production (`hoangmai.dev`):**
> - Frontend: `https://spyglass.hoangmai.dev` — Cloudflare Worker `spyglass-web` (Static Assets).
> - API: `https://api-spyglass.hoangmai.dev` — proxied CNAME to tunnel `spyglass-prod` → `http://server:8080`.
> - Backend host: elitedesk, rootless Podman, `systemd --user` unit `spyglass.service`.
> - Old `cyc.fyi` hostnames are expired; the old Pages project is dormant.
>
> The relay (`proxy-spyglass.cyc.fyi`) is legacy and was not migrated.

---

## 💻 Residential Node Setup (The Exit Node)
This must be running for the Remote Server to bypass blocks. This can be any machine at your home (Mac, Windows, Linux, Raspberry Pi) that runs Docker.

### Step 1: Environment Variables
Ensure you have a `.env` file in the project directory on this machine:
```text
CLOUDFLARED_TOKEN=your_token_here
```

### Step 2: Launch the Relay
Run the following command on the residential machine:
```bash
docker compose -f docker-compose.proxy.yml up -d --build
```
*   **Why:** This starts the Node.js relay and the `cloudflared` tunnel agent.

---

## 🌐 Remote Server Setup (The Application)
This is the main search engine.

### Step 1: Build (From Local Mac)
```bash
podman build --platform linux/amd64 -t docker.io/cyclone1070/spyglass-backend:hoangmai .
```

### Step 2: Deploy to elitedesk
```bash
podman save docker.io/cyclone1070/spyglass-backend:hoangmai | ssh cyc@100.73.151.89 'podman load'
ssh cyc@100.73.151.89 'systemctl --user restart spyglass.service'
```

The server's kube manifest (`/home/cyc/.config/containers/systemd/spyglass.yaml`) uses the `:hoangmai` tag. Keep it unique: `podman kube play` pulls `:latest` from Docker Hub, which would replace a locally loaded image. If you push to a registry, use a unique tag and update the manifest.

## 🌐 Frontend (Cloudflare Workers)

The Vite/React SPA is served by Worker `spyglass-web`, not Pages.

- Production: `https://spyglass.hoangmai.dev`
- API base: `https://api-spyglass.hoangmai.dev` (`VITE_API_URL`, baked at build time)
- Config: `wrangler.jsonc` — assets from `dist`, `not_found_handling: "single-page-application"`, custom domain
- Deploy: `VITE_API_URL=https://api-spyglass.hoangmai.dev pnpm build && pnpm run deploy`

## 🔀 API Routing

`api-spyglass.hoangmai.dev` is a proxied CNAME to `2989cdfd-8509-4092-9889-027bc1951413.cfargotunnel.com`, which routes through tunnel `spyglass-prod` to `http://server:8080`. The tunnel's ingress is managed remotely; do not change the DNS record independently of the tunnel config.

---

## 🛠 Local Development (Alternative)
If you want to run the entire stack locally for testing without the Remote Server:
```bash
docker compose -f docker-compose.dev.yml up -d --build
```
*   **Why:** This starts the backend and an isolated local MongoDB instance for development testing.

---

## 📝 Key Endpoints
*   **Trigger Initial Scrape:** `POST /api/link`
*   **Search Aggregator:** `GET /api/search?q=query`
*   **Mac Relay Status:** `http://localhost:3128/fetch?url=https://google.com` (Local only)

---

## 🔑 Cloudflare Credentials (local only)

Deployment/agent automation reads these from the repo-root `.env`, which is gitignored and must never be committed:

*   `CLOUDFLARE_API_TOKEN` — Account: `Workers Scripts Edit` + `Cloudflare Tunnel Edit`; Zone: `Workers Routes Edit` + `DNS Edit` + `Zone Read`
*   `CLOUDFLARE_ACCOUNT_ID`
*   `CLOUDFLARE_ZONE_ID`

`wrangler` needs them exported. Do not `source .env` (the Mongo URI contains shell-special characters); use:

```bash
export CLOUDFLARE_API_TOKEN=$(grep -E '^CLOUDFLARE_API_TOKEN=' .env | cut -d= -f2-)
export CLOUDFLARE_ACCOUNT_ID=$(grep -E '^CLOUDFLARE_ACCOUNT_ID=' .env | cut -d= -f2-)
export CLOUDFLARE_ZONE_ID=$(grep -E '^CLOUDFLARE_ZONE_ID=' .env | cut -d= -f2-)
```
