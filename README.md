# nebula-md Markdown Editor

Windows Markdown editor and live previewer.[^license]

## Install on Windows with Chocolatey

Open PowerShell as an administrator and run:

```powershell
choco install nebula-md -y
```

Chocolatey installs nebula-md, adds it to the Start menu, and registers it as an option for opening Markdown files. To install a newer version when one is available, run:

```powershell
choco upgrade nebula-md -y
```

## Features

- Open a Markdown file with the **Open** button, `Ctrl+O`, drag-and-drop, or by passing a file path to the executable.
- Edit source and see the rendered result update live.
- Switch between source, split, and preview layouts.
- Render tables, task lists, footnotes, fenced code blocks, and other extended Markdown.
- Resolve relative images and links from the Markdown file's folder.
- Automatically refresh when another program changes the open file.
- Toggle paper and midnight preview themes.
- Save with `Ctrl+S`, reload with `F5`, and print with `Ctrl+P`.

## Windows “Open with” integration

Run `Register-nebula-md-FileAssociations.ps1` from the published application folder to add **nebula-md** to the Windows **Open with** list for `.md`, `.markdown`, and `.mdown` files. The registration applies only to the current Windows user and does not replace their existing default app.

Run `Unregister-nebula-md-FileAssociations.ps1` to remove the integration.

Raw HTML is intentionally displayed as text rather than executed, preventing scripts embedded in a Markdown file from running inside the preview.

## Build

```powershell
dotnet build
dotnet publish -c Release -r win-x64 --self-contained true
```

The published app uses the Microsoft Edge WebView2 Runtime included with current Windows 10 and Windows 11 installations.

[^license]: nebula-md is open-source software distributed under the [MIT License](LICENSE).
