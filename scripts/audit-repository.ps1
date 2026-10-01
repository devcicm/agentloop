param(
    [string]$RepositoryRoot = (Join-Path $PSScriptRoot '..'),
    [string]$OutputPath = '.agentloop/repository-audit.json'
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
$output = [IO.Path]::GetFullPath((Join-Path $root $OutputPath))
if (-not $output.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'El inventario debe guardarse dentro del repositorio.'
}
$relativeOutput = $output.Substring($root.Length + 1).Replace('\', '/')
$paths = @(& git -C $root -c core.quotepath=false ls-files --cached --others --exclude-standard) | Sort-Object -Unique
if ($LASTEXITCODE -ne 0) { throw 'No se pudo obtener el inventario Git.' }
$errors = [Collections.Generic.List[object]]::new()
$files = [Collections.Generic.List[object]]::new()
$links = [Collections.Generic.List[object]]::new()
$historicalDrift = [Collections.Generic.List[object]]::new()
if ($PSVersionTable.PSVersion.Major -lt 6) {
    Add-Type -AssemblyName System.Web.Extensions
    $jsonReader = [Web.Script.Serialization.JavaScriptSerializer]::new()
    $jsonReader.MaxJsonLength = 16777216
}
function Assert-Json([string]$text) {
    # Windows PowerShell ConvertFrom-Json confunde claves validas Thinking/thinking.
    if ($PSVersionTable.PSVersion.Major -lt 6) { $null = $jsonReader.DeserializeObject($text) }
    else { $null = $text | ConvertFrom-Json -AsHashtable -ErrorAction Stop }
}

function Get-Category([string]$name) {
    switch -Regex ($name) {
        '^local-secrets/' { return 'guia-credenciales' }
        '^docs/evidence/' { return 'evidencia-historica' }
        '^docs/historico/' { return 'informe-historico' }
        '^docs/(analisis-|auditoria-|validacion-)' { return 'informe-historico' }
        '^docs/' { return 'documentacion-vigente' }
        '^scripts/' { return 'herramienta-mantenimiento' }
        '(SelfTest|Probe)\.cs$' { return 'comprobacion-local' }
        '^(Looping/|StateMachines/.*StateMachine|Goals/EventDrivenAgent)' { return 'ejecutor-compatibilidad' }
        '\.cs$' { return 'codigo-compilado' }
        default { return 'configuracion-o-indice' }
    }
}

foreach ($name in $paths) {
    $path = Join-Path $root $name
    if ($name -eq $relativeOutput) { continue } # Evitar hashes autorreferentes.
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        $errors.Add([ordered]@{ Kind = 'missing-file'; Path = $name })
        continue
    }
    $info = Get-Item -LiteralPath $path
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    $files.Add([pscustomobject][ordered]@{ Path = $name; Category = Get-Category $name; Bytes = $info.Length; SHA256 = $hash })
    if ($name.EndsWith('.md')) {
        $text = [IO.File]::ReadAllText($path)
        foreach ($match in [regex]::Matches($text, '\]\(([^)]+)\)')) {
            $href = $match.Groups[1].Value.Split('#')[0].Trim('<', '>')
            if (-not $href -or $href -match '^[a-zA-Z]+:' -or $href.StartsWith('/')) { continue }
            $target = [IO.Path]::GetFullPath((Join-Path $info.DirectoryName $href))
            $exists = ($target -eq $output) -or (Test-Path -LiteralPath $target)
            $links.Add([ordered]@{ Source = $name; Target = $href; Exists = $exists })
            if (-not $exists) { $errors.Add([ordered]@{ Kind = 'broken-link'; Path = $name; Target = $href }) }
        }
    }
    if ($name.EndsWith('.json')) {
        try { Assert-Json ([IO.File]::ReadAllText($path)) }
        catch { $errors.Add([ordered]@{ Kind = 'invalid-json'; Path = $name }) }
    }
    if ($name.EndsWith('.jsonl')) {
        $lineNumber = 0
        foreach ($line in [IO.File]::ReadLines($path)) {
            $lineNumber++
            try { Assert-Json $line }
            catch { $errors.Add([ordered]@{ Kind = 'invalid-jsonl'; Path = $name; Line = $lineNumber }) }
        }
    }
}

$manifestPath = Join-Path $root 'docs/evidence/2026-09-27/evidence-manifest.json'
if (Test-Path -LiteralPath $manifestPath) {
    foreach ($entry in ([IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json)) {
        $current = $files | Where-Object { $_.Path -eq $entry.Path } | Select-Object -First 1
        if (-not $current -or $current.SHA256 -ne $entry.SHA256) {
            $historicalDrift.Add([ordered]@{ Path = $entry.Path; Reason = 'foto-historica-no-es-manifiesto-actual' })
        }
    }
}
$duplicates = @($files | Group-Object SHA256 | Where-Object Count -gt 1 | ForEach-Object { ,@($_.Group.Path) })
$localGroups = foreach ($name in @('bin', 'obj', '.vs', '.agentloop', 'artifacts', 'notes', 'local-secrets')) {
    $dir = Join-Path $root $name
    $items = @(if (Test-Path -LiteralPath $dir) { Get-ChildItem -LiteralPath $dir -Recurse -File -Force -ErrorAction SilentlyContinue })
    $sum = ($items | Measure-Object Length -Sum).Sum
    if ($null -eq $sum) { $sum = 0 }
    $disposition = switch ($name) {
        'bin' { 'salida-regenerable-no-versionada' }
        'obj' { 'salida-regenerable-no-versionada' }
        '.vs' { 'estado-IDE-conservado' }
        '.agentloop' { 'estado-operativo-y-archivo-conservados' }
        'artifacts' { 'catalogos-generados-y-journeys-persistidos' }
        'notes' { 'directorio-de-trabajo-local' }
        'local-secrets' { 'credenciales-conservadas-sin-inspeccionar-contenido' }
    }
    $localFiles = @(if ($name -ne 'local-secrets') {
        foreach ($item in $items) {
            [ordered]@{ Path = $item.FullName.Substring($root.Length + 1).Replace('\', '/'); Bytes = $item.Length }
        }
    })
    [ordered]@{ Directory = $name; Files = $items.Count; Bytes = $sum; ContentInspected = $false; Disposition = $disposition; FileMetadata = $localFiles }
}
$report = [ordered]@{
    CapturedAt = [DateTimeOffset]::UtcNow.ToString('o')
    SourceCommit = (& git -C $root rev-parse HEAD)
    Scope = 'archivos-Git-y-nuevos-no-ignorados; datos-locales-solo-metadatos'
    InventoryExcludesItself = $relativeOutput
    FileCount = $files.Count
    Files = @($files.ToArray())
    LocalDirectories = @($localGroups)
    LocalLinks = @($links.ToArray())
    ExactDuplicateGroups = $duplicates
    HistoricalManifestDrift = @($historicalDrift.ToArray())
    Errors = @($errors.ToArray())
}
$parent = Split-Path -Parent $output
$null = New-Item -ItemType Directory -Path $parent -Force
$serialized = ($report | ConvertTo-Json -Depth 12).Replace("`r`n", "`n")
[IO.File]::WriteAllText($output, $serialized + "`n", [Text.UTF8Encoding]::new($false))
Write-Output ('Inventario: {0}; archivos: {1}; errores: {2}; duplicados exactos: {3}' -f $relativeOutput, $files.Count, $errors.Count, $duplicates.Count)
if ($errors.Count -gt 0) { exit 1 }
