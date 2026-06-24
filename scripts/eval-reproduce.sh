#!/usr/bin/env bash
# One-command keyless retrieval-eval reproduction.
# By default runs against the committed curated corpus (always present, no download).
# If data/juristcu/ exists it switches to the JurisTCU dataset automatically.
#
# Usage:
#   bash scripts/eval-reproduce.sh
#
# Output: Recall@K, Hit-rate@K, MRR printed to stdout; API process stopped on exit.

set -euo pipefail

PORT=${PORT:-5007}
STARTUP_TIMEOUT=${STARTUP_TIMEOUT:-90}
REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"

export ASPNETCORE_ENVIRONMENT=Testing
export ASPNETCORE_URLS="http://localhost:$PORT"
export Rag__TopK=${TOPK:-10}   # match the k=10 figures reported in Docs/eval-datasets.md

JURIS_PATH="$REPO_ROOT/data/juristcu"
if [ -f "$JURIS_PATH/doc.csv" ]; then
    export Eval__Dataset=juristcu
    export Eval__JurisTcuPath="$JURIS_PATH"
    echo "Dataset: JurisTCU ($JURIS_PATH)"
else
    unset Eval__Dataset 2>/dev/null || true
    unset Eval__JurisTcuPath 2>/dev/null || true
    echo "Dataset: curated corpus (committed)"
fi

echo "Starting API on port $PORT (keyless fakes)..."
dotnet run --no-launch-profile --project "$REPO_ROOT/src/LexRag.Api" &
API_PID=$!

stop_api() { kill "$API_PID" 2>/dev/null || true; }
trap stop_api EXIT

# Wait for the API to become ready.
deadline=$(( $(date +%s) + STARTUP_TIMEOUT ))
ready=0
while [ "$(date +%s)" -lt "$deadline" ]; do
    if curl -sf "http://localhost:$PORT/health" > /dev/null 2>&1; then
        ready=1
        break
    fi
    sleep 0.5
done

if [ "$ready" -eq 0 ]; then
    echo "ERROR: API did not start within ${STARTUP_TIMEOUT}s." >&2
    exit 1
fi

echo "API ready. Running /eval/retrieval..."
result=$(curl -sf -X POST "http://localhost:$PORT/eval/retrieval")

echo ""
echo "=== Retrieval results ==="
echo "$result" | python3 -c "
import json, sys
d = json.load(sys.stdin)
print(f'  K              : {d[\"k\"]}')
print(f'  Total queries  : {d[\"total\"]}')
print(f'  Hit-rate@K     : {d[\"hitRateAtK\"]:.1%}')
print(f'  Recall@K       : {d[\"recallAtK\"]:.1%}')
print(f'  MRR            : {d[\"mrr\"]:.3f}')
if d.get('byGroup'):
    print()
    print('  Per-group:')
    for g in d['byGroup']:
        print(f'    {g[\"group\"]} (n={g[\"n\"]}): hit-rate {g[\"hitRateAtK\"]:.0%}  recall {g[\"recallAtK\"]:.0%}  MRR {g[\"mrr\"]:.3f}')
print()
print(d['summary'])
" || echo "$result"
