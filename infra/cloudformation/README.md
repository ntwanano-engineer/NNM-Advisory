# NNM Advisory - AWS deployment (CloudFormation)

Four standalone stacks deployed in order. Each stack exports values the next one
consumes, threaded automatically by `deploy.sh`.

| # | Stack            | Template          | Creates |
|---|------------------|-------------------|---------|
| 1 | `nnm-network`    | `01-network.yaml` | VPC, 2 public + 2 private subnets, IGW, NAT gateway, routes |
| 2 | `nnm-data`       | `02-data.yaml`    | RDS PostgreSQL 16, private S3 images bucket, API + RDS security groups, `/nnm/*` SSM params |
| 3 | `nnm-api`        | `03-api.yaml`     | ECS Fargate cluster/service, internet-facing ALB, IAM roles, CloudWatch log group |
| 4 | `nnm-frontend`   | `04-frontend.yaml`| Private S3 web bucket + CloudFront (OAC, SPA routing) |

## Why this maps to the app

- The API auto-loads config from SSM path `/nnm` in Production (`Program.cs`), so
  the task definition only sets non-secret env vars (`ASPNETCORE_ENVIRONMENT=Production`,
  `EMAIL_PROVIDER=ses`, `IMAGE_STORAGE_PROVIDER=s3`, `AWS_REGION`).
- Kestrel listens on `:8080`; the ALB target group health check is `/health`.
- Leadership photos are stored privately in S3 and served via presigned URLs.
- Migrations are NOT automatic - run the one-off `migrate` task after `api`.

## Prerequisites

- AWS CLI v2, authenticated (`aws sts get-caller-identity`)
- Docker (to build/push the API image)
- Node.js (to build the frontend bundle)
- Recommended: `pip install cfn-lint` to validate templates locally

## Deploy

```bash
cd infra/cloudformation
cp deploy.env.example deploy.env     # fill in DB_PASSWORD, JWT_SECRET, emails, CORS origin
source deploy.env

# optional: validate first
cfn-lint *.yaml

./deploy.sh network
./deploy.sh build-push      # builds api/Dockerfile, pushes to ECR, prints API_IMAGE_URI
# put that API_IMAGE_URI into deploy.env, then: source deploy.env
./deploy.sh data
./deploy.sh api
./deploy.sh migrate         # one-off RUN_MIGRATIONS=true task; watch /ecs/nnm-api logs
./deploy.sh frontend
./deploy.sh upload-web      # npm build + s3 sync + CloudFront invalidation
```

Smoke test:

```bash
curl -i  http://<alb-dns>/health         # 200
curl -I  https://<cloudfront-domain>/     # 200
curl -I  https://<cloudfront-domain>/about  # 200 via SPA fallback
```

## Important notes

- **SecureString:** CloudFormation cannot create SecureString SSM parameters, so
  `02-data.yaml` writes `/nnm/db/connection-string` and `/nnm/jwt/secret` as
  plain `String`. After the data stack deploys, convert them:

  ```bash
  aws ssm put-parameter --name /nnm/jwt/secret --type SecureString --overwrite \
    --value "$JWT_SECRET"
  aws ssm put-parameter --name /nnm/db/connection-string --type SecureString --overwrite \
    --value "$(aws ssm get-parameter --name /nnm/db/connection-string --query Parameter.Value --output text)"
  ```

  The task role already grants `kms:Decrypt` on `alias/aws/ssm` for this.

- **TLS / domains are optional.** Leave `API_CERTIFICATE_ARN`, `WEB_CERTIFICATE_ARN`,
  and `WEB_DOMAIN_NAME` empty for a first HTTP/CloudFront-default deploy. The ALB
  then serves HTTP on :80; add the regional ACM cert later to enable HTTPS + redirect.
  CloudFront's cert must be in **us-east-1**.

- **SES starts in sandbox.** Verify the sender/domain and request production access
  before contact emails reach arbitrary recipients.

- **Photos:** upload real images to the keys the seed migration expects, e.g.
  `s3://<images-bucket>/leadership/ntwanano-mabasa.jpeg`, or re-upload via the Admin page.

## Billable resources

RDS, Fargate tasks, ALB, NAT gateway, and CloudFront all incur ongoing cost. The
NAT gateway + ALB are the main fixed monthly charges. To cut NAT cost, replace it
with VPC endpoints for ECR, S3, SSM, and CloudWatch Logs.

## Teardown (reverse order)

```bash
# empty retained buckets first (web + images), then:
aws cloudformation delete-stack --stack-name nnm-frontend
aws cloudformation delete-stack --stack-name nnm-api
aws cloudformation delete-stack --stack-name nnm-data      # RDS takes a final snapshot
aws cloudformation delete-stack --stack-name nnm-network
```

The data and frontend buckets use `DeletionPolicy: Retain` and RDS uses
`Snapshot`, so data is not lost on an accidental stack delete - clean those up
manually when you intend to.
---

# GitHub + CI/CD (deploy from GitHub to AWS)

The app deploys automatically from GitHub Actions using OIDC - no AWS access
keys are stored in GitHub. Two workflows live in `.github/workflows/`:

| Workflow      | Trigger                     | Does |
|---------------|-----------------------------|------|
| `ci.yml`      | push / PR to `main`         | API build+test, frontend typecheck+test, `cfn-lint` |
| `deploy.yml`  | push to `main` / manual run | build+push API image, roll ECS, run migrations, build+sync frontend, invalidate CloudFront |

Infrastructure (the four stacks) is still deployed once from a workstation with
`deploy.sh`. CI/CD then ships *application* changes on top of that infra.

## One-time setup

### 1. Check the code into GitHub

```bash
cd "NNM Advisory"
git init
git add .
git commit -m "Initial commit: NNM Advisory app + infra"
git branch -M main

# Create the PRIVATE repo and push. With the GitHub CLI:
gh repo create <org>/nnm-advisory --private --source=. --remote=origin --push

# ...or manually: create an empty private repo on github.com, then:
git remote add origin git@github.com:<org>/nnm-advisory.git
git push -u origin main
```

`.gitignore` already excludes secrets and personal files: `api/.env`,
`infra/cloudformation/deploy.env`, `frontend/.env.production`, `*.pdf`, the
root `*.jfif`/pic files, and `*.docx`. The owner photos the app uses live in
`frontend/public/` and ARE committed.

### 2. Create the GitHub OIDC deploy role (CloudFormation)

```bash
aws cloudformation deploy \
  --region af-south-1 \
  --stack-name nnm-github-oidc \
  --template-file infra/cloudformation/00-github-oidc.yaml \
  --capabilities CAPABILITY_NAMED_IAM \
  --parameter-overrides GitHubOrg=<org> GitHubRepo=nnm-advisory CreateOidcProvider=true

# Grab the role ARN:
aws cloudformation describe-stacks --stack-name nnm-github-oidc \
  --query "Stacks[0].Outputs[?OutputKey=='RoleArn'].OutputValue" --output text
```

If your account already has a `token.actions.githubusercontent.com` OIDC
provider, set `CreateOidcProvider=false`. The trust policy only allows
`repo:<org>/nnm-advisory:ref:refs/heads/main` to assume the role.

### 3. Add the GitHub secret + environment

- Repo -> Settings -> Secrets and variables -> Actions -> New repository secret:
  - `AWS_DEPLOY_ROLE_ARN` = the RoleArn from step 2.
- Repo -> Settings -> Environments -> create `production` (optionally add required
  reviewers so deploys need approval).

## Order of operations (first launch)

1. Deploy infra from a workstation: `./deploy.sh network`, `build-push`, `data`,
   `api`, `migrate`, `frontend` (see the earlier section). This creates the
   stacks the deploy workflow reads outputs from.
2. Push to GitHub (step 1 above). CI runs.
3. Merge/push to `main` -> `deploy.yml` runs automatically, or trigger it from
   the Actions tab (Run workflow).

After infra exists, every push to `main` redeploys the app end to end. The
workflow resolves all resource names from CloudFormation stack outputs, so there
is nothing hardcoded to keep in sync.

## Notes

- `deploy.yml` builds the frontend with `VITE_API_URL=https://<alb-dns>`. Once you
  put the API behind a custom domain + HTTPS, change that line (or add an
  `API_DOMAIN` secret) so the browser calls the HTTPS endpoint.
- The migration step runs a one-off task and fails the deploy if its exit code is
  non-zero, so a bad migration stops the rollout.
- `concurrency: deploy-prod` prevents overlapping deploys.
