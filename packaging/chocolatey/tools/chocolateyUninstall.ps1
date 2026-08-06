$ErrorActionPreference = 'Stop'

$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$shortcutPath = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\nebula-md.lnk'
$unregistrationScript = Join-Path $toolsDir 'Unregister-nebula-md-FileAssociations.ps1'

if (Test-Path -LiteralPath $unregistrationScript -PathType Leaf) {
    & $unregistrationScript
}

if (Test-Path -LiteralPath $shortcutPath -PathType Leaf) {
    Remove-Item -LiteralPath $shortcutPath -Force
}

Write-Host 'nebula-md has been uninstalled.'
