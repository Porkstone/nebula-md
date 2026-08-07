# nebula-md Markdown Editor

Windows Markdown editor and live previewer.[^license]

## Why was this application built

It was built to allow quick previews and small edits to Markdown files on Windows. Other editors like Cursor felt unnecessarily heavy.

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

Chocolatey registers nebula-md as an option for opening Markdown files automatically. When using the portable executable, run the following command from its folder to add nebula-md to the Windows **Open with** list for `.md`, `.markdown`, and `.mdown` files:

```powershell
.\nebula-md.exe --register-file-associations
```

The registration applies only to the current Windows user and does not replace their existing default app. To remove the integration, run:

```powershell
.\nebula-md.exe --unregister-file-associations
```

Raw HTML is intentionally displayed as text rather than executed, preventing scripts embedded in a Markdown file from running inside the preview.

## Build

```powershell
dotnet build
dotnet publish -c Release -r win-x64 --self-contained true
```

The published app uses the Microsoft Edge WebView2 Runtime included with current Windows 10 and Windows 11 installations.

[^license]: nebula-md is open-source software distributed under the [MIT License](LICENSE).
