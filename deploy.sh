#!/bin/bash
set -e

# Configuration
VPS_HOST="root@46.224.118.74"
REMOTE_DIR="/opt/serenada"

echo "🚀 Starting production deployment..."

# 1. Build the frontend
echo "📦 Building frontend..."
(cd client && npm run build)

# 2. Sync files to VPS
echo "📤 Syncing files to VPS..."
# Use -R to create relative paths on destination (e.g. client/dist/ -> /opt/connected/client/dist/)
rsync -avzR \
    --exclude 'server/server' \
    --exclude 'server/server_test' \
    docker-compose.yml \
    docker-compose.prod.yml \
    .env.production \
    server/ \
    client/dist/ \
    nginx/ \
    coturn/ \
    "$VPS_HOST:$REMOTE_DIR/"

# 3. Copy production env file and restart services
echo "🔄 Restarting production services..."
ssh "$VPS_HOST" "cd $REMOTE_DIR && \
    cp .env.production .env && \
    docker compose -f docker-compose.yml -f docker-compose.prod.yml down && \
    docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --build"

# 4. Verify deployment
echo "✅ Verifying deployment..."
sleep 3
ssh "$VPS_HOST" "docker ps"
curl -sI https://serenada.app | head -n 1

echo ""
echo "🎉 Deployment complete! App is live at https://serenada.app"
