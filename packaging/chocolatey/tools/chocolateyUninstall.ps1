$ErrorActionPreference = 'Stop'

$toolsDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$executablePath = Join-Path $toolsDir 'nebula-md.exe'
$shortcutPath = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\nebula-md.lnk'

if (Test-Path -LiteralPath $executablePath -PathType Leaf) {
    Start-ChocolateyProcessAsAdmin `
        -Statements '--unregister-file-associations' `
        -ExeToRun $executablePath `
        -ValidExitCodes @(0) `
        -WorkingDirectory $toolsDir
}

if (Test-Path -LiteralPath $shortcutPath -PathType Leaf) {
    Remove-Item -LiteralPath $shortcutPath -Force
}

Write-Host 'nebula-md has been uninstalled.'
