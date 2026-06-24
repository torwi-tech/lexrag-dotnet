#!/usr/bin/env pwsh
# One-command keyless retrieval-eval reproduction.
# By default runs against the committed curated corpus (always present, no download).
# If data/juristcu/ exists it switches to the JurisTCU dataset automatically.
#
# Usage:
#   ./scripts/eval-reproduce.ps1
#
# Output: Recall@K, Hit-rate@K, MRR printed to stdout; API process stopped on exit.

param(
    [int]$Port = 5007,
    [int]$StartupTimeoutSeconds = 90
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent

$env:ASPNETCORE_ENVIRONMENT = "Testing"
$env:ASPNETCORE_URLS        = "http://localhost:$Port"
$env:Rag__TopK              = "10"   # match the k=10 figures reported in Docs/eval-datasets.md

# Switch to JurisTCU when the dataset is present locally (it is large and not committed).
$jurisPath = Join-Path $repoRoot "data" "juristcu"
if (Test-Path (Join-Path $jurisPath "doc.csv")) {
    $env:Eval__Dataset       = "juristcu"
    $env:Eval__JurisTcuPath  = $jurisPath
    Write-Host "Dataset: JurisTCU ($jurisPath)"
} else {
    Remove-Item Env:Eval__Dataset       -ErrorAction SilentlyContinue
    Remove-Item Env:Eval__JurisTcuPath  -ErrorAction SilentlyContinue
    Write-Host "Dataset: curated corpus (committed)"
}

Write-Host "Starting API on port $Port (keyless fakes)..."
$apiProc = Start-Process dotnet `
    -ArgumentList "run --no-launch-profile --project `"$repoRoot/src/LexRag.Api`"" `
    -PassThru -WindowStyle Hidden

function Stop-Api {
    if (-not $apiProc.HasExited) { $apiProc | Stop-Process -Force }
}

# Wait for the API to become ready.
$deadline = [DateTime]::UtcNow.AddSeconds($StartupTimeoutSeconds)
$ready = $false
while ([DateTime]::UtcNow -lt $deadline) {
    try {
        $null = Invoke-WebRequest "http://localhost:$Port/health" -UseBasicParsing -ErrorAction Stop
        $ready = $true
        break
    } catch {
        Start-Sleep -Milliseconds 500
    }
}

if (-not $ready) {
    Stop-Api
    Write-Error "API did not start within $StartupTimeoutSeconds s."
}

Write-Host "API ready. Running /eval/retrieval..."
try {
    $resp = Invoke-RestMethod "http://localhost:$Port/eval/retrieval" -Method Post
    Write-Host ""
    Write-Host "=== Retrieval results ==="
    Write-Host ("  K              : " + $resp.k)
    Write-Host ("  Total queries  : " + $resp.total)
    Write-Host ("  Hit-rate@K     : " + ("{0:P1}" -f $resp.hitRateAtK))
    Write-Host ("  Recall@K       : " + ("{0:P1}" -f $resp.recallAtK))
    Write-Host ("  MRR            : " + ("{0:F3}" -f $resp.mrr))
    if ($resp.byGroup) {
        Write-Host ""
        Write-Host "  Per-group:"
        foreach ($g in $resp.byGroup) {
            Write-Host ("    {0,-22} hit-rate {1:P0}  recall {2:P0}  MRR {3:F3}" `
                -f ($g.group + " (n=" + $g.n + ")"), $g.hitRateAtK, $g.recallAtK, $g.mrr)
        }
    }
    Write-Host ""
    Write-Host $resp.summary
} finally {
    Stop-Api
}
