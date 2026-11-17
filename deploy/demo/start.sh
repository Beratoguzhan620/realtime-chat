#!/bin/sh
# Redis in memory only (presence and the backplane need nothing kept), then the API in the foreground.
set -e
redis-server --bind 127.0.0.1 --port 6379 --save "" --appendonly no --dir /tmp --daemonize yes --logfile /tmp/redis.log
until redis-cli -h 127.0.0.1 ping >/dev/null 2>&1; do sleep 0.2; done
exec dotnet /app/RealtimeChat.Api.dll
