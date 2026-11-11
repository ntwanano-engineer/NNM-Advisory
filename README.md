# NNM Advisory Website

Professional services company profile website for NNM Advisory, built with React (frontend), .NET 8 (backend API), and PostgreSQL (database).

## Repository Structure

```
NNM Advisory/
├── frontend/        React SPA (Vite + TypeScript)
├── api/             .NET 8 REST API
├── infra/           AWS infrastructure as code (CloudFormation / CDK)
├── docs/            Runbook and specification documents
├── docker-compose.yml
└── README.md
```

## Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (includes Docker Compose v2)
- Git

No other tools need to be installed locally — the Dockerfiles handle the .NET SDK and Node.js.

## Local Development Setup

### 1 — Clone the repository

```bash
git clone <repo-url>
cd "NNM Advisory"
```

**Expected output:** repository cloned, working directory set.

### 2 — Create the API environment file

```bash
cp api/.env.sample api/.env
```

Open `api/.env` and replace the placeholder values with real local credentials if needed. The defaults work as-is with Docker Compose (the `db` service uses matching credentials).

**Expected output:** `api/.env` created. No output from the shell command itself.

### 3 — Build and start all services

```bash
docker compose up --build
```

**Expected output:** Docker builds both images, then streams interleaved logs from three services:

```
db-1        | database system is ready to accept connections
api-1       | Starting NNM Advisory API
api-1       | Now listening on: http://[::]:8080
frontend-1  | VITE v5.x  ready in Xs
frontend-1  | ➜  Local:   http://localhost:3000/
```

All three components run concurrently in the same terminal session. Press `Ctrl+C` to stop.

### 4 — Apply database migrations

In a second terminal (while `docker compose up` is running):

```bash
docker compose exec api dotnet ef database update
```

**Expected output:**

```
Build started...
Build succeeded.
Applying migration '20240101000000_InitialCreate'.
Done.
```

### 5 — Verify the health check

```bash
curl http://localhost:5000/health
```

**Expected output:**

```
HTTP/1.1 200 OK
{"status":"Healthy"}
```

All three components are operational when this returns HTTP 200.

---

## Environment Variables

All configurable values live in `api/.env` (copied from `api/.env.sample`). The application halts at startup and prints the name of each missing variable if any required variable is absent.

| Variable | Description |
|---|---|
| `DB_CONNECTION_STRING` | Npgsql connection string to PostgreSQL |
| `JWT_SECRET` | HS256 signing secret (min 32 characters) |
| `EMAIL_PROVIDER` | `smtp` (local) or `ses` (AWS) |
| `SMTP_HOST` | SMTP server hostname (local dev: `mailhog`) |
| `SMTP_PORT` | SMTP server port (local dev: `1025`) |
| `SENDER_EMAIL` | From address for outbound emails |
| `ADMIN_EMAIL` | Company inbox for admin notification emails |
| `IMAGE_STORAGE_PROVIDER` | `local` (filesystem) or `s3` (AWS) |
| `S3_IMAGES_BUCKET` | S3 bucket name for leadership photos |
| `CORS_ALLOWED_ORIGIN` | Frontend origin allowed by the CORS policy |

The `api/.env` file is excluded from version control via `.gitignore`. Never commit real credentials.

---

## Running Tests

**Backend (.NET — xUnit + CsCheck):**

```bash
docker compose exec api dotnet test
```

**Frontend (Vitest + fast-check):**

```bash
docker compose exec frontend npm test
```

---

## Useful Commands

| Goal | Command |
|---|---|
| Start all services (detached) | `docker compose up -d` |
| View API logs | `docker compose logs -f api` |
| Stop and remove containers | `docker compose down` |
| Stop and remove containers + volumes | `docker compose down -v` |
| Rebuild after code changes | `docker compose up --build` |
| Open a psql shell | `docker compose exec db psql -U nnm -d nnmdb` |

---

## AWS Deployment

See [`docs/aws-runbook.md`](docs/aws-runbook.md) for step-by-step deployment, update, rollback, and troubleshooting instructions.
