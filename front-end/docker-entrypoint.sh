#!/bin/sh
# Sobrescreve config.json com a URL da API em runtime.
# Com BaseUrl="" as chamadas /api/* funcionam como URLs relativas via Nginx proxy.
# Defina FRONTEND_API_BASE_URL para usar uma URL absoluta (ex: https://api.meudominio.com).
API_BASE_URL=${FRONTEND_API_BASE_URL:-""}

cat > /usr/share/nginx/html/assets/config.json <<EOF
{
    "BaseUrl": "${API_BASE_URL}"
}
EOF

echo "[entrypoint] config.json atualizado: BaseUrl='${API_BASE_URL}'"

exec nginx -g "daemon off;"
