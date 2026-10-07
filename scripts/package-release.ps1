[CmdletBinding()]
param(
    [string]$Runtime = 'win-x64',
    [string]$OutputDirectory = '.agentloop/release',
    [switch]$AllowUnlicensedPreview
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$releaseRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location -LiteralPath $releaseRoot
try {
    if ($Runtime -ne 'win-x64') { throw 'Solo win-x64 esta admitido para este candidato.' }
    git diff --quiet
    if ($LASTEXITCODE -ne 0) { throw 'Commit de fuentes requerido: hay cambios tracked sin registrar.' }
    git diff --cached --quiet
    if ($LASTEXITCODE -ne 0) { throw 'Commit de fuentes requerido: hay cambios staged.' }
    $releaseCommit = (git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $releaseCommit -notmatch '^[a-f0-9]{40}$') { throw 'No se pudo identificar commit.' }
    [xml]$releaseProperties = Get-Content -LiteralPath 'Directory.Build.props' -Raw
    $releaseVersion = [string]$releaseProperties.Project.PropertyGroup.Version
    $releaseRuntimeVersion = [string]$releaseProperties.Project.PropertyGroup.ReleaseRuntimeVersion
    if ($releaseRuntimeVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'Version del runtime de distribucion invalida.' }
    if ($releaseVersion -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') { throw 'Version invalida.' }
    $releaseLicense = Test-Path -LiteralPath 'LICENSE' -PathType Leaf
    if (-not $releaseLicense -and -not $AllowUnlicensedPreview) { throw 'Licencia pendiente. Solo el candidato local admite -AllowUnlicensedPreview.' }
    if (-not $releaseLicense -and $releaseVersion -notmatch '-') { throw 'No se admite version estable sin licencia elegida.' }
    $releaseOutput = [IO.Path]::GetFullPath($(if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $releaseRoot $OutputDirectory }))
    $releaseLocalBoundary = [IO.Path]::GetFullPath((Join-Path $releaseRoot '.agentloop')) + [IO.Path]::DirectorySeparatorChar
    if (-not $releaseOutput.StartsWith($releaseLocalBoundary, [StringComparison]::OrdinalIgnoreCase)) { throw 'La salida debe estar dentro de .agentloop/.' }
    $releaseSdk = (dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'SDK no disponible.' }
    $releaseWork = Join-Path $releaseOutput ('staging-' + [Guid]::NewGuid().ToString('N'))
    $releaseStage = Join-Path $releaseWork 'package'
    New-Item -ItemType Directory -Path $releaseStage -Force | Out-Null
    dotnet publish src/AgentLoop.Cli/AgentLoop.Cli.csproj -c Release -r $Runtime --self-contained true -o $releaseStage "-p:SourceRevisionId=$releaseCommit" "-p:RuntimeFrameworkVersion=$releaseRuntimeVersion"
    if ($LASTEXITCODE -ne 0) { throw 'Publish local fallo.' }
    git diff --quiet
    if ($LASTEXITCODE -ne 0) { throw 'Las fuentes cambiaron durante publish.' }
    git diff --cached --quiet
    if ($LASTEXITCODE -ne 0 -or (git rev-parse HEAD).Trim() -ne $releaseCommit) { throw 'El candidato cambio durante publish.' }
    # Copiar solo recursos declarados. Nunca copiar el workspace entero ni archivos no versionados.
    $releaseDocuments = @('README.md','CHANGELOG.md','SECURITY.md','CONTRIBUTING.md','docs/proposito-y-alcance.md',
        'docs/guides/setup-credenciales.md','docs/plans/mejoras-agenticas-2026-10-07.md','docs/guides/instalacion.md','docs/guides/comandos-permisos-edicion.md','docs/guides/seguridad-datos-harness.md',
        'docs/guides/diagnostico-no-cierre.md','docs/architecture/flujos-operativos.md','docs/architecture/core-kernel.md',
        'docs/architecture/goals-proyectos.md','docs/plans/publicacion-github-2026-10-07.md',
        'examples/contracts/minimal.example.json','examples/profiles/artifacts.example.json','examples/profiles/docker.template.json')
    if ($releaseLicense) { $releaseDocuments += 'LICENSE' }
    foreach ($releaseDocument in $releaseDocuments) {
        git ls-files --error-unmatch -- $releaseDocument | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Recurso no versionado: $releaseDocument" }
        $releaseTarget = Join-Path $releaseStage $releaseDocument
        New-Item -ItemType Directory -Path (Split-Path -Parent $releaseTarget) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $releaseRoot $releaseDocument) -Destination $releaseTarget
    }
    if (-not $releaseLicense) {
        [IO.File]::WriteAllText((Join-Path $releaseStage 'LICENSING-STATUS.txt'), 'Licencia pendiente. Candidato local no autorizado para publicacion o redistribucion como open source.', [Text.UTF8Encoding]::new($false))
    }
    $releaseFiles = @(Get-ChildItem -LiteralPath $releaseStage -File -Recurse | Sort-Object FullName | ForEach-Object {
        @{ path = $_.FullName.Substring($releaseStage.Length + 1).Replace('\','/'); bytes = $_.Length;
           sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
    $releaseManifest = [ordered]@{
        schemaVersion = 1; product = 'AgentLoop'; version = $releaseVersion; intendedTag = "v$releaseVersion";
        commit = $releaseCommit; sdk = $releaseSdk; runtimeIdentifier = $Runtime; runtimeVersion = $releaseRuntimeVersion; selfContained = $true;
        licenseStatus = $(if ($releaseLicense) { 'selected' } else { 'pending' }); published = $false;
        validation = 'Build/publish local. Consultar informe de evidencia del candidato para pruebas; no se infieren de este script.';
        limitations = @('Preview .NET 9; migracion a .NET 10 pendiente','Shell no es sandbox','Selftest requiere checkout y SDK','Sin firma de autor');
        files = $releaseFiles
    }
    $releaseManifestText = $releaseManifest | ConvertTo-Json -Depth 8
    $releaseEncoding = [Text.UTF8Encoding]::new($false)
    [IO.File]::WriteAllText((Join-Path $releaseStage 'release-manifest.json'), $releaseManifestText, $releaseEncoding)
    [IO.File]::WriteAllText((Join-Path $releaseWork 'release-manifest.json'), $releaseManifestText, $releaseEncoding)
    $releaseNotes = "# AgentLoop v$releaseVersion`n`nCandidato local sin publicacion. Commit: $releaseCommit. RID: $Runtime. SDK: $releaseSdk.`n`nLicencia: $($releaseManifest.licenseStatus). Ver CHANGELOG, SECURITY e instalacion. No se ejecutan pruebas remotas desde este script.`n"
    [IO.File]::WriteAllText((Join-Path $releaseWork 'release-notes.md'), $releaseNotes, $releaseEncoding)
    $releaseZip = Join-Path $releaseWork "AgentLoop-v$releaseVersion-$Runtime.zip"
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($releaseStage, $releaseZip, [IO.Compression.CompressionLevel]::Optimal, $false)
    python scripts/audit-release.py --zip $releaseZip --report (Join-Path $releaseWork 'package-audit.json')
    if ($LASTEXITCODE -ne 0) { throw 'Auditoria del paquete fallo; no distribuir.' }
    $releaseSums = @(Get-ChildItem -LiteralPath $releaseWork -File | Where-Object { $_.Name -ne 'SHA256SUMS.txt' } | Sort-Object Name | ForEach-Object {
        (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.Name
    }) -join "`n"
    [IO.File]::WriteAllText((Join-Path $releaseWork 'SHA256SUMS.txt'), $releaseSums + "`n", $releaseEncoding)
    Write-Output "Candidato local: $releaseWork"
    Write-Output 'No se creo tag, remoto, push, release ni publicacion.'
} finally { Pop-Location }
