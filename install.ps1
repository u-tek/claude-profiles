# Builds Claude Profiles and installs it for the current user (no admin needed).
$ErrorActionPreference = 'Stop'
$dest = Join-Path $env:LOCALAPPDATA 'Programs\ClaudeProfiles'

Get-Process ClaudeProfiles -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

dotnet publish "$PSScriptRoot\ClaudeProfiles.csproj" -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -p:DebugType=none -o $dest | Out-Host

$exe = Join-Path $dest 'ClaudeProfiles.exe'
$shell = New-Object -ComObject WScript.Shell
foreach ($dir in @([Environment]::GetFolderPath('Desktop'), (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'))) {
    $lnk = $shell.CreateShortcut((Join-Path $dir 'Claude Profiles.lnk'))
    $lnk.TargetPath = $exe
    $lnk.WorkingDirectory = $dest
    $lnk.IconLocation = "$exe,0"
    $lnk.Description = 'Run separate Claude profiles side by side'
    $lnk.Save()
}
Write-Host "Installed to $dest"
