$ErrorActionPreference = "Stop"
$repoRoot   = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$cliProj    = Join-Path $repoRoot "src\Mail2Pst.Cli\Mail2Pst.Cli.csproj"
$publishDir = Join-Path $PSScriptRoot "..\src-tauri\.sidecar-publish"
$binDir     = Join-Path $PSScriptRoot "..\src-tauri\binaries"
$resDir     = Join-Path $PSScriptRoot "..\src-tauri\resources"

Write-Host "Publishing CLI sidecar (single-file, self-contained)..."
# Delete the C# intermediate output (obj/) for the CLI and Core projects first so the publish
# always recompiles from current source. Guards against MSBuild's timestamp-based incremental
# check republishing a stale Core.dll when file mtimes are unreliable (e.g. synced/networked
# working copies). `dotnet publish` does not accept `--no-incremental`, so clean obj/ instead.
foreach ($objDir in @(
  (Join-Path $repoRoot "src\Mail2Pst.Core\obj"),
  (Join-Path $repoRoot "src\Mail2Pst.Cli\obj")
)) {
  if (Test-Path $objDir) { Remove-Item -Recurse -Force $objDir }
}
dotnet publish $cliProj -c Release -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:PublishTrimmed=false `
  -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

New-Item -ItemType Directory -Force -Path $binDir | Out-Null
New-Item -ItemType Directory -Force -Path $resDir | Out-Null

Copy-Item (Join-Path $publishDir "Mail2Pst.Cli.exe") `
  (Join-Path $binDir "mail2pst-cli-x86_64-pc-windows-msvc.exe") -Force
Copy-Item (Join-Path $repoRoot "fixtures\sample.mbox") `
  (Join-Path $resDir "sample.mbox") -Force

Write-Host "Sidecar + resource staged."
