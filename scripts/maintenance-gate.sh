#!/usr/bin/env bash
# Local, read-only maintenance gate. Never deploys, never writes production.
set -euo pipefail

ROOT="${SPYGLASS_ROOT:-$HOME/repos}"
FRONTEND="${SPYGLASS_FRONTEND:-$ROOT/spyglass}"
BACKEND="${SPYGLASS_BACKEND:-$ROOT/spyglass-backend}"
VITE_API_URL="${VITE_API_URL:-https://api-spyglass.hoangmai.dev}"

PNPM=(npx --yes pnpm@10)

step() {
	printf '\n==> %s\n' "$*"
}

step "Frontend install"
cd "$FRONTEND"
"${PNPM[@]}" install --frozen-lockfile

step "Frontend lint"
"${PNPM[@]}" lint

step "Frontend tests"
"${PNPM[@]}" test

step "Frontend build"
VITE_API_URL="$VITE_API_URL" "${PNPM[@]}" build

step "Backend restore"
cd "$BACKEND"
dotnet restore --nologo
dotnet restore spyglass-backend.Tests --nologo

step "Backend build"
dotnet build --no-restore --nologo

step "Backend tests"
dotnet test spyglass-backend.Tests --no-restore --nologo

step "Gate green"
printf 'Verified %s and %s at %s\n' "$FRONTEND" "$BACKEND" "$(date -u '+%Y-%m-%dT%H:%M:%SZ')"
