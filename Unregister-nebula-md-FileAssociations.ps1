$ErrorActionPreference = 'Stop'

$classesRoot = 'HKCU:\Software\Classes'
$targets = @(
    (Join-Path $classesRoot 'Applications\nebula-md.exe'),
    (Join-Path $classesRoot 'nebula-md.Markdown')
)

foreach ($target in $targets) {
    if (Test-Path -LiteralPath $target) {
        Remove-Item -LiteralPath $target -Recurse -Force
    }
}

foreach ($extension in @('.md', '.markdown', '.mdown')) {
    $openWithKey = Join-Path $classesRoot ($extension + '\OpenWithProgids')
    if (Test-Path -LiteralPath $openWithKey) {
        Remove-ItemProperty -LiteralPath $openWithKey -Name 'nebula-md.Markdown' -ErrorAction SilentlyContinue
    }
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

Write-Host 'nebula-md file integration has been removed.'
