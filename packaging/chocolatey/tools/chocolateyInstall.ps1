$ErrorActionPreference = 'Stop'

$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$executablePath = Join-Path $toolsDir 'nebula-md.exe'
$guiMarkerPath = "$executablePath.gui"
$skipAutoUninstallerPath = Join-Path $toolsDir '.skipAutoUninstaller'
$shortcutPath = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\nebula-md.lnk'
$registrationScript = Join-Path $toolsDir 'Register-nebula-md-FileAssociations.ps1'

if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw "nebula-md executable was not found: $executablePath"
}

New-Item -Path $guiMarkerPath -ItemType File -Force | Out-Null
New-Item -Path $skipAutoUninstallerPath -ItemType File -Force | Out-Null

Install-ChocolateyShortcut `
    -ShortcutFilePath $shortcutPath `
    -TargetPath $executablePath `
    -WorkingDirectory $toolsDir

& $registrationScript -ExecutablePath $executablePath

Write-Host 'nebula-md has been installed.'
