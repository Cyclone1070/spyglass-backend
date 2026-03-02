# Spyglass Deployment Guide

This project follows a **Hybrid Deployment Strategy** to bypass datacenter IP blocking (like Cloudflare) by routing specific requests through a residential network (**Residential Node**).

---

## 🏗 Architecture Overview
1.  **Remote Server (AWS):** Runs the main .NET backend and connects to the production MongoDB Atlas.
2.  **Residential Node (Home PC/Mac/Pi):** Runs a custom Node.js relay server and a Cloudflare Tunnel. It provides the residential IP address used for "forbidden" requests.
3.  **Request Flow:** If the Remote Server receives a `403 Forbidden` from a target site, it automatically retries the request through the Residential Node relay via the Cloudflare Tunnel (`proxy-spyglass.cyc.fyi`).

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

### Step 1: Build & Push (From Local Mac)
Whenever you update the code, build the image and push it to Docker Hub:
```bash
docker buildx build --push -t cyclone1070/spyglass-backend:latest --provenance=false .
```

### Step 2: Deploy to Remote
On the Remote Server, ensure you have the `docker-compose.yml` file, then run:
```bash
docker compose pull
docker compose up -d
```
*   **Why:** This pulls the latest image and starts the backend service.

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
