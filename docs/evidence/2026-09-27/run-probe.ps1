param([string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path)
$ErrorActionPreference = 'Stop'
$assembly = Join-Path $RepositoryRoot 'bin/Debug/net9.0/AgentLoop.dll'
if (-not (Test-Path -LiteralPath $assembly)) {
    throw 'Compile primero AgentLoop.csproj con dotnet build.'
}
$probeDirectory = Join-Path ([IO.Path]::GetTempPath()) ('agentloop-audit-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $probeDirectory | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'HarnessProbe.cs.txt') -Destination (Join-Path $probeDirectory 'Program.cs')
$escapedAssembly = [Security.SecurityElement]::Escape($assembly)
$projectXml = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup><Reference Include="AgentLoop"><HintPath>$escapedAssembly</HintPath></Reference></ItemGroup>
</Project>
"@
$project = Join-Path $probeDirectory 'HarnessProbe.csproj'
[IO.File]::WriteAllText($project, $projectXml)
dotnet build $project --nologo --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'No se pudo compilar la sonda.' }
$probeOutput = Join-Path $PSScriptRoot 'harness-probe.json'
$probeWorkingDirectory = Join-Path $probeDirectory 'workspace'
dotnet (Join-Path $probeDirectory 'bin/Debug/net9.0/HarnessProbe.dll') $probeWorkingDirectory | Set-Content -Encoding UTF8 -LiteralPath $probeOutput
if ($LASTEXITCODE -ne 0) { throw 'La sonda no termino correctamente.' }
Get-Content -Encoding UTF8 -LiteralPath $probeOutput
