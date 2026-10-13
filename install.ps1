# Builds Claude Profiles from source and installs it for the current user (no admin needed).
$ErrorActionPreference = 'Stop'
$out = Join-Path $PSScriptRoot 'dist'

dotnet publish "$PSScriptRoot\ClaudeProfiles.csproj" -c Release -o $out | Out-Host
if ($LASTEXITCODE) { throw 'Build failed.' }

# The built exe installs itself into %LOCALAPPDATA%\Programs\ClaudeProfiles, adds shortcuts, and opens.
& (Join-Path $out 'ClaudeProfiles.exe')
