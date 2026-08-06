param(
    [string]$ExecutablePath = (Join-Path $PSScriptRoot 'nebula-md.exe')
)

$ErrorActionPreference = 'Stop'
$resolvedExecutable = (Resolve-Path -LiteralPath $ExecutablePath -ErrorAction Stop).Path
if ([System.IO.Path]::GetExtension($resolvedExecutable) -ne '.exe') {
    throw "Expected a Windows executable: $resolvedExecutable"
}

$classesRoot = 'HKCU:\Software\Classes'
$applicationKey = Join-Path $classesRoot 'Applications\nebula-md.exe'
$progIdKey = Join-Path $classesRoot 'nebula-md.Markdown'
$command = '"' + $resolvedExecutable + '" "%1"'

New-Item -Path $applicationKey -Force | Out-Null
New-ItemProperty -Path $applicationKey -Name 'FriendlyAppName' -Value 'nebula-md' -PropertyType String -Force | Out-Null
New-ItemProperty -Path $applicationKey -Name 'ApplicationDescription' -Value 'Preview and edit Markdown files in nebula-md.' -PropertyType String -Force | Out-Null

$applicationCommandKey = Join-Path $applicationKey 'shell\open\command'
New-Item -Path $applicationCommandKey -Force | Out-Null
Set-Item -Path $applicationCommandKey -Value $command

$supportedTypesKey = Join-Path $applicationKey 'SupportedTypes'
New-Item -Path $supportedTypesKey -Force | Out-Null
foreach ($extension in @('.md', '.markdown', '.mdown')) {
    New-ItemProperty -Path $supportedTypesKey -Name $extension -Value '' -PropertyType String -Force | Out-Null
}

New-Item -Path $progIdKey -Force | Out-Null
Set-Item -Path $progIdKey -Value 'Markdown document'
New-ItemProperty -Path $progIdKey -Name 'FriendlyTypeName' -Value 'Markdown document' -PropertyType String -Force | Out-Null

$iconKey = Join-Path $progIdKey 'DefaultIcon'
New-Item -Path $iconKey -Force | Out-Null
Set-Item -Path $iconKey -Value ('"' + $resolvedExecutable + '",0')

$progIdCommandKey = Join-Path $progIdKey 'shell\open\command'
New-Item -Path $progIdCommandKey -Force | Out-Null
Set-Item -Path $progIdCommandKey -Value $command

foreach ($extension in @('.md', '.markdown', '.mdown')) {
    $openWithKey = Join-Path $classesRoot ($extension + '\OpenWithProgids')
    New-Item -Path $openWithKey -Force | Out-Null
    New-ItemProperty -Path $openWithKey -Name 'nebula-md.Markdown' -Value '' -PropertyType String -Force | Out-Null
}

if (-not ('ShellAssociationRefresh' -as [type])) {
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class ShellAssociationRefresh
{
    [DllImport("shell32.dll")]
    public static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}
'@
}
[ShellAssociationRefresh]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)

Write-Host "nebula-md is registered for Markdown files."
Write-Host "Executable: $resolvedExecutable"
