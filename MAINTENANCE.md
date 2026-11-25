# Weekly maintenance runbook

Scheduled for **Sunday 01:00 UTC** — one hour after the production scrape at
00:00 UTC (12:00 Sunday in Sydney during daylight saving, 11:00 in winter).

The production container runs in UTC. Do not change the scrape schedule or the
production data as part of maintenance.

## Hard rules

- The gate is **local and read-only**: install, lint, test, build. It must never
  write to production MongoDB, call a production write endpoint, or deploy.
- Never call `POST /api/link`; it deletes all links before scraping. Never
  restart the production service outside the release step below.
- Release only after the gate is green. Deploy at most one candidate at a time.
- Never print, commit, or paste secrets. Load Cloudflare credentials only with
  `source "$HOME/repos/dotfiles/zsh/secrets.zsh"`.
- **100 goal rounds is a ceiling, not a target.** Stop as soon as the work is
  green and verified. Do not burn rounds to use them up.

## 0. Create the goal

Create one goal for this run with `max_goal_rounds` = **100**:

> Run the weekly Spyglass maintenance window: verify the Sunday scrape, update
> dependencies, pass the local gate, release only if green, and canary the live
> site.

Use `update_goal` to mark it complete when done, or blocked only after a genuine
blocker persists.

## 1. Set up the run folder

Use a UTC date so it matches the production scrape:

```bash
RUN_DIR="/tmp/spyglass-maintenance/$(date -u +%F)"
mkdir -p "$RUN_DIR"
```

Keep logs and the final report in `$RUN_DIR`.

## Email blocker alerts

Email is specific to Spyglass, so its settings do **not** belong in the global
dotfiles secrets. Cloudflare credentials there are shared across projects; this
app password is not.

The notifier loads a project-scoped file, `~/repos/spyglass-backend/.env.maintenance`
by default. This is separate from the app’s `.env`: `.env` is loaded by the .NET
process and contains runtime credentials, while the notifier is a shell script.
Loading `.env` into the shell would pull Mongo and proxy credentials into it for
no reason.

Create it with mode `600`:

```bash
chmod 600 "$HOME/repos/spyglass-backend/.env.maintenance"
```

Add:

```bash
SPYGLASS_SMTP_HOST=smtp.gmail.com
SPYGLASS_SMTP_PORT=587
SPYGLASS_SMTP_USER=hoangmai1070@gmail.com
SPYGLASS_SMTP_PASSWORD=the-16-character-app-password
SPYGLASS_EMAIL_FROM=hoangmai1070@gmail.com
SPYGLASS_EMAIL_TO=hoangmai1070@gmail.com
```

`SPYGLASS_MAINTENANCE_ENV` can point the notifier at a different file if needed.
The file is gitignored by `.env.*`, excluded from the Docker build context, and
read only by the notifier.

Do not reuse the app password in another project. Other projects may send from
the same Gmail account, but each should have its own app password so revoking
one does not break the others.

Port 465 uses implicit TLS; other ports use STARTTLS. Gmail, iCloud, Fastmail,
and Resend SMTP all work with these variables. Use an app-specific password, not
your primary account password.

Test it once after configuring:

```bash
cd "$HOME/repos/spyglass-backend"
./scripts/notify-blocker.sh \
  "Spyglass maintenance test" \
  "The blocker notification path is configured."
```

## 2. Verify the Sunday scrape (read-only)

The production scraper deletes all existing links and then repopulates the
collection. Never trigger it manually while diagnosing.

```bash
ssh -o BatchMode=yes -o ConnectTimeout=10 cyc@100.73.151.89 \
  'journalctl --user -u spyglass.service --since "6 hours ago" --no-pager' \
  > "$RUN_DIR/scrape.log" 2>&1 || true

grep -E 'Starting scheduled scrape|Successfully scraped and saved|Error occurred during scheduled scrape|Next scrape scheduled' \
  "$RUN_DIR/scrape.log" | tail -20
```

Record:

- the start line;
- the latest `Successfully scraped and saved <N> links` line and `N`;
- any scrape error.

If SSH is unavailable, record that and continue with the local gate. If the
scrape failed, diagnose read-only (logs, service status, network) and record
the evidence. Do **not** trigger a new scrape.

## 3. Run the baseline gate

```bash
cd "$HOME/repos/spyglass-backend"
./scripts/maintenance-gate.sh 2>&1 | tee "$RUN_DIR/gate-baseline.log"
```

The gate runs:

- frontend frozen install, lint, tests, production build;
- backend restore, build, tests.

If baseline is red, fix the local cause and rerun **before** updating anything.
Do not deploy while the gate is red.

## 4. Update dependencies

Do this only after a green baseline.

### Frontend

```bash
cd "$HOME/repos/spyglass"
npx --yes pnpm@10 outdated --format json > "$RUN_DIR/frontend-outdated.json" 2>&1 || true
npx --yes pnpm@10 update
npx --yes pnpm@10 audit --audit-level high > "$RUN_DIR/frontend-audit.txt" 2>&1 || true
```

`pnpm update` updates within the existing semver ranges. Take security and
patch/minor updates automatically.

Do **not** use `--latest` to take major versions automatically. A major update
is allowed only when it installs cleanly and the full gate passes **without
source code changes**. If it needs code changes, skip it and record why.

### Backend

```bash
cd "$HOME/repos/spyglass-backend"
dotnet list package --outdated > "$RUN_DIR/backend-outdated.txt" 2>&1 || true
dotnet list spyglass-backend.Tests package --outdated > "$RUN_DIR/backend-tests-outdated.txt" 2>&1 || true
dotnet list package --vulnerable --include-transitive > "$RUN_DIR/backend-vulnerable.txt" 2>&1 || true
dotnet list spyglass-backend.Tests package --vulnerable --include-transitive >> "$RUN_DIR/backend-vulnerable.txt" 2>&1 || true
```

Update patch/minor/security versions with `dotnet add package <id> --version
<version>`. For a transitive warning, update the direct package that pulls it in,
or add a direct reference to a fixed version if that is the only safe option.
Skip majors unless the full gate passes without code changes. Check the test
project too; it has its own package set.

Both projects should report no vulnerable packages. If an advisory reappears,
find the direct package that pulls it in. The previous `SharpCompress` and
`Snappier` advisories came through `MongoDB.Driver`.

## 5. Re-run the gate and repair

```bash
cd "$HOME/repos/spyglass-backend"
./scripts/maintenance-gate.sh 2>&1 | tee "$RUN_DIR/gate-final.log"
```

If red, diagnose the actual failure, make local fixes, and rerun. There is no
arbitrary two-attempt limit. Stop when:

- the gate is green;
- the same failure persists across rounds with no new information or progress;
- the 100-round goal ceiling is reached.

If stopped red, do not commit, push, or deploy. Save the logs and report.

## 6. Release, only if green and there are changes

Check each repo:

```bash
cd "$HOME/repos/spyglass" && git status --short && git diff --check
cd "$HOME/repos/spyglass-backend" && git status --short && git diff --check
```

Scan the diff for secrets and unintended production data:

```bash
git diff | grep -iE 'CLOUDFLARE_API_TOKEN|cfut_|MONGO_CONNECTION_STRING' && {
  echo "Possible secret in diff; stopping"; exit 1;
}
```

Stage only intended source and lockfile changes. Never commit `.env`, logs,
`bin/`, `obj/`, or `node_modules/`.

Commit and push each changed repo:

```bash
git add <intended files>
git commit -m "chore: weekly dependency maintenance $(date -u +%F)"
git push
```

### Deploy frontend

```bash
cd "$HOME/repos/spyglass"
source "$HOME/repos/dotfiles/zsh/secrets.zsh"
VITE_API_URL=https://api-spyglass.hoangmai.dev npx --yes pnpm@10 build
VITE_API_URL=https://api-spyglass.hoangmai.dev npx --yes pnpm@10 run deploy 2>&1 | tee "$RUN_DIR/frontend-deploy.log"
```

Record the Worker version ID printed by Wrangler.

### Deploy backend, only if backend files changed

```bash
cd "$HOME/repos/spyglass-backend"
podman build --platform linux/amd64 \
  -t docker.io/cyclone1070/spyglass-backend:hoangmai .
podman save docker.io/cyclone1070/spyglass-backend:hoangmai \
  | ssh cyc@100.73.151.89 'podman load'
ssh cyc@100.73.151.89 'systemctl --user restart spyglass.service'
```

This is the only step allowed to restart production. If the build or load fails,
stop before restarting anything. The backend `.dockerignore` excludes `.env`,
tests, and local build output; do not remove those exclusions.

## 7. Canary (read-only)

```bash
curl -fsS -o /dev/null -w 'frontend %{http_code}\n' https://spyglass.hoangmai.dev/
curl -fsS -o /dev/null -w 'api %{http_code}\n' \
  'https://api-spyglass.hoangmai.dev/api/search?q=firefox'
curl -fsS -o /dev/null -w 'favicon %{http_code}\n' \
  'https://spyglass.hoangmai.dev/favicon.svg?v=20261125'
```

All three should be 2xx. The search request is a read-only GET; do not POST.
If a canary fails, stop deploying, preserve the version IDs and logs, and report
the failure. Do not attempt further releases until the cause is understood.

## 8. Report and finish

Write `$RUN_DIR/summary.md` with:

- scrape status and link count;
- baseline and final gate results;
- dependency changes, including skipped majors and why;
- commits and pushes;
- frontend Worker version and backend image/restart status;
- canary results;
- anything blocked or left for the user.

Attach or link the run folder in the final message. Mark the goal complete if the
gate is green and release/canary succeeded. If blocked, leave the goal open or
mark it blocked with the concrete blocker; do not commit, push, or deploy.

## Genuine blockers

Stop and report, rather than guessing, when:

- credentials or SSH access are unavailable;
- production is unhealthy before any changes;
- a required major update breaks the gate and cannot be repaired safely;
- the same failure has no progress across multiple rounds;
- a release canary fails.

Before marking the goal blocked, send the email alert:

```bash
cd "$HOME/repos/spyglass-backend"
./scripts/notify-blocker.sh \
  "Spyglass maintenance blocked: <short reason>" \
  "Blocker: <what happened>
Run report: $RUN_DIR/summary.md
Last gate: $RUN_DIR/gate-final.log
No commit, push, or deploy was made." \
  || echo "WARNING: blocker email failed; record that in the report"
```

If the email fails, still record the blocker and mention the notification failure
in the report. Do not let a failed notification trigger a release.
