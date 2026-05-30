#!/bin/bash
set -e

echo "[entrypoint] Aguardando SQL Server em db:1433..."

MAX_RETRIES=30
RETRY_DELAY=5
RETRIES=0

until (echo > /dev/tcp/db/1433) 2>/dev/null; do
    RETRIES=$((RETRIES + 1))
    if [ $RETRIES -ge $MAX_RETRIES ]; then
        echo "[entrypoint] ERRO: SQL Server nao ficou disponivel apos $((MAX_RETRIES * RETRY_DELAY))s."
        exit 1
    fi
    echo "[entrypoint] Tentativa $RETRIES/$MAX_RETRIES — aguardando ${RETRY_DELAY}s..."
    sleep $RETRY_DELAY
done

echo "[entrypoint] SQL Server disponivel. Iniciando API..."
exec dotnet Ares.PagueAres.API.dll
