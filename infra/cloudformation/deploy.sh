#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# NNM Advisory - CloudFormation deploy orchestrator.
#
# Deploys four standalone stacks in order, threading outputs between them:
#   1. network   -> VPC, subnets, NAT
#   2. data      -> RDS, S3 images bucket, SSM /nnm/* params
#   3. api       -> ECS Fargate + ALB + IAM (needs API_IMAGE_URI)
#   4. frontend  -> S3 + CloudFront
#
# Prereqs: AWS CLI v2, Docker, and deploy.env (copy from deploy.env.example).
# Usage:
#   source deploy.env
#   ./deploy.sh network
#   ./deploy.sh build-push        # build+push the API image to ECR
#   ./deploy.sh data
#   ./deploy.sh api
#   ./deploy.sh migrate           # one-off RUN_MIGRATIONS task
#   ./deploy.sh frontend
#   ./deploy.sh upload-web        # build + sync the React bundle, invalidate CF
#   ./deploy.sh all               # network, data, api, frontend (no build steps)
# ---------------------------------------------------------------------------
set -euo pipefail

: "${AWS_REGION:?set AWS_REGION (source deploy.env)}"
: "${APP:=nnm}"
ACCOUNT_ID=$(aws sts get-caller-identity --query Account --output text)
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"

out() { # stack-name output-key
  aws cloudformation describe-stacks --region "$AWS_REGION" --stack-name "$1" \
    --query "Stacks[0].Outputs[?OutputKey=='$2'].OutputValue" --output text
}

deploy() { # stack-name template-file [params...]
  local name="$1"; local tpl="$2"; shift 2
  echo ">> deploying $name"
  aws cloudformation deploy \
    --region "$AWS_REGION" \
    --stack-name "$name" \
    --template-file "$HERE/$tpl" \
    --capabilities CAPABILITY_NAMED_IAM CAPABILITY_IAM \
    --no-fail-on-empty-changeset \
    --parameter-overrides "$@"
}

case "${1:-}" in
  network)
    deploy "${APP}-network" 01-network.yaml AppName="$APP"
    ;;

  data)
    VPC=$(out "${APP}-network" VpcId)
    PRIV=$(out "${APP}-network" PrivateSubnets)
    deploy "${APP}-data" 02-data.yaml \
      AppName="$APP" VpcId="$VPC" PrivateSubnets="$PRIV" \
      DbPassword="${DB_PASSWORD:?}" JwtSecret="${JWT_SECRET:?}" \
      SenderEmail="${SENDER_EMAIL:?}" AdminEmail="${ADMIN_EMAIL:?}" \
      CorsAllowedOrigin="${CORS_ALLOWED_ORIGIN:?}"
    ;;

  build-push)
    aws ecr describe-repositories --repository-names "${APP}-api" --region "$AWS_REGION" >/dev/null 2>&1 \
      || aws ecr create-repository --repository-name "${APP}-api" --region "$AWS_REGION" >/dev/null
    aws ecr get-login-password --region "$AWS_REGION" \
      | docker login --username AWS --password-stdin "${ACCOUNT_ID}.dkr.ecr.${AWS_REGION}.amazonaws.com"
    IMG="${ACCOUNT_ID}.dkr.ecr.${AWS_REGION}.amazonaws.com/${APP}-api:latest"
    docker build --platform linux/amd64 -t "$IMG" "$REPO_ROOT/api"
    docker push "$IMG"
    echo "pushed $IMG"
    echo "Set API_IMAGE_URI=$IMG in deploy.env before ./deploy.sh api"
    ;;

  api)
    VPC=$(out "${APP}-network" VpcId)
    PUB=$(out "${APP}-network" PublicSubnets)
    PRIV=$(out "${APP}-network" PrivateSubnets)
    APISG=$(out "${APP}-data" ApiSecurityGroupId)
    BUCKET=$(out "${APP}-data" ImagesBucketName)
    deploy "${APP}-api" 03-api.yaml \
      AppName="$APP" VpcId="$VPC" PublicSubnets="$PUB" PrivateSubnets="$PRIV" \
      ApiSecurityGroupId="$APISG" ImagesBucketName="$BUCKET" \
      ApiImageUri="${API_IMAGE_URI:?set API_IMAGE_URI (run build-push first)}" \
      CertificateArn="${API_CERTIFICATE_ARN:-}"
    echo "API ALB: http://$(out "${APP}-api" AlbDnsName)/health"
    ;;

  migrate)
    CLUSTER=$(out "${APP}-api" ClusterName)
    TASKDEF=$(out "${APP}-api" TaskDefinitionArn)
    APISG=$(out "${APP}-data" ApiSecurityGroupId)
    PRIV=$(out "${APP}-network" PrivateSubnets)
    echo ">> running one-off migration task"
    aws ecs run-task --region "$AWS_REGION" --cluster "$CLUSTER" \
      --task-definition "$TASKDEF" --launch-type FARGATE \
      --network-configuration "awsvpcConfiguration={subnets=[${PRIV}],securityGroups=[${APISG}],assignPublicIp=DISABLED}" \
      --overrides '{"containerOverrides":[{"name":"api","environment":[{"name":"RUN_MIGRATIONS","value":"true"}]}]}'
    echo "Watch the task log group /ecs/${APP}-api until it exits 0."
    ;;

  frontend)
    deploy "${APP}-frontend" 04-frontend.yaml \
      AppName="$APP" \
      CertificateArn="${WEB_CERTIFICATE_ARN:-}" DomainName="${WEB_DOMAIN_NAME:-}"
    echo "CloudFront: https://$(out "${APP}-frontend" DistributionDomainName)"
    ;;

  upload-web)
    BUCKET=$(out "${APP}-frontend" WebBucketName)
    DIST=$(out "${APP}-frontend" DistributionId)
    API_URL="https://$(out "${APP}-api" AlbDnsName)"
    [ -n "${WEB_DOMAIN_NAME:-}" ] && echo "Note: build uses API_URL=$API_URL; set a custom API domain if desired."
    ( cd "$REPO_ROOT/frontend"
      echo "VITE_API_URL=$API_URL" > .env.production
      npm ci
      npm run build
      aws s3 sync dist/ "s3://$BUCKET/" --delete --region "$AWS_REGION" )
    aws cloudfront create-invalidation --distribution-id "$DIST" --paths "/*" >/dev/null
    echo "uploaded + invalidated"
    ;;

  all)
    "$0" network; "$0" data; "$0" api; "$0" frontend
    ;;

  *)
    echo "usage: $0 {network|build-push|data|api|migrate|frontend|upload-web|all}"
    exit 1
    ;;
esac