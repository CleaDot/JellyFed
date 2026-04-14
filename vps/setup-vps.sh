#!/usr/bin/env bash
# setup-vps.sh — Configuration initiale du repo JellyFed sur le VPS
# À exécuter UNE SEULE FOIS depuis blynk :
#   bash vps/setup-vps.sh
#
# Prérequis :
#   - DNS jellyfed.bly-net.com → 194.99.23.234 propagé
#   - SSH alias vps-blynet configuré dans ~/.ssh/config

set -euo pipefail

VPS="vps-blynet"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

echo "==> Création du dossier repo sur le VPS..."
ssh "$VPS" "mkdir -p /srv/jellyfed-repo"

echo "==> Déploiement config nginx HTTP-only (bootstrap certbot)..."
ssh "$VPS" "cat > /etc/nginx/sites-available/jellyfed.bly-net.com" << 'NGINX_HTTP'
server {
    listen 80;
    server_name jellyfed.bly-net.com;
    root /srv/jellyfed-repo;

    location /.well-known/acme-challenge/ {
        root /var/www/html;
    }

    location /repo/ {
        alias /srv/jellyfed-repo/;
        autoindex off;
    }

    location = / {
        return 200 'JellyFed Plugin Repository\n';
        add_header Content-Type text/plain;
    }
}
NGINX_HTTP

ssh "$VPS" "ln -sf /etc/nginx/sites-available/jellyfed.bly-net.com /etc/nginx/sites-enabled/jellyfed.bly-net.com"
ssh "$VPS" "nginx -t && systemctl reload nginx"
echo "    nginx rechargé (HTTP)"

echo ""
echo "==> Obtention du certificat SSL Let's Encrypt..."
ssh "$VPS" "certbot --nginx -d jellyfed.bly-net.com --non-interactive --agree-tos -m admin@bly-net.com"
echo "    Certificat obtenu"

echo ""
echo "==> Déploiement config nginx finale (HTTPS)..."
scp "$SCRIPT_DIR/jellyfed.bly-net.com.nginx" "$VPS:/etc/nginx/sites-available/jellyfed.bly-net.com"
ssh "$VPS" "nginx -t && systemctl reload nginx"
echo "    nginx rechargé (HTTPS)"

echo ""
echo "==> Setup terminé !"
echo "    URL repo : https://jellyfed.bly-net.com/repo/manifest.json"
echo ""
echo "==> Prochaine étape : déployer les fichiers du repo"
echo "    ./scripts/build-repo.sh --deploy"
