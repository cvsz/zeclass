param(
  [ValidateSet("win-x64", "win-arm64", "win-x86", "all")]
  [string]$Runtime = "all",
  [string]$Configuration = "Release",
  [string]$OutputRoot = ""
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $projectRoot "src\zEClass\zEClass.csproj"
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
  $OutputRoot = Join-Path $projectRoot "artifacts"
}

$runtimes = if ($Runtime -eq "all") { @("win-x64", "win-arm64", "win-x86") } else { @($Runtime) }

Write-Host "Building zEClass for: $($runtimes -join ', ')" -ForegroundColor Cyan

foreach ($rid in $runtimes) {
  $out = Join-Path $OutputRoot $rid
  Write-Host "`n==> $rid" -ForegroundColor Yellow

  # Restore must be forced per runtime: the win-x64 graph is not covered by a plain restore.
  dotnet restore $project -r $rid --force-evaluate
  if ($LASTEXITCODE -ne 0) { throw "restore failed for $rid" }

  dotnet publish $project -c $Configuration -r $rid --self-contained true -o $out --no-restore
  if ($LASTEXITCODE -ne 0) { throw "publish failed for $rid" }

  $exe = Join-Path $out "zEClass.exe"
  if (-not (Test-Path -LiteralPath $exe)) {
    throw "publish for $rid produced no zEClass.exe"
  }

  $hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash
  $size = [math]::Round((Get-Item -LiteralPath $exe).Length / 1MB, 1)
  Write-Host "    zEClass.exe  $size MB  sha256=$hash"

  @"
$rid
$Configuration
built: $(Get-Date -Format o)
zEClass.exe sha256: $hash
"@ | Set-Content -LiteralPath (Join-Path $out "BUILDINFO.txt") -Encoding utf8
}

Write-Host "`nArtifacts in $OutputRoot" -ForegroundColor Green
Get-ChildItem $OutputRoot -Directory | ForEach-Object {
  $info = Join-Path $_.FullName "BUILDINFO.txt"
  if (Test-Path $info) { Write-Host "  $($_.Name)" }
}
