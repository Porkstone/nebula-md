using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace NebulaMd;

internal sealed class MainForm : Form
{
    private static readonly Color Ink = Color.FromArgb(28, 30, 27);
    private static readonly Color DeepInk = Color.FromArgb(20, 21, 19);
    private static readonly Color Lichen = Color.FromArgb(171, 183, 157);
    private static readonly Color Ochre = Color.FromArgb(218, 166, 65);
    private static readonly Color Paper = Color.FromArgb(246, 241, 230);
    private static readonly Color Muted = Color.FromArgb(139, 143, 133);

    private readonly MarkdownPipeline pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    private readonly TextBox editor = new();
    private readonly WebView2 preview = new();
    private readonly SplitContainer split = new();
    private readonly Label fileLabel = new();
    private readonly Label statusLabel = new();
    private readonly Label metricsLabel = new();
    private readonly Panel externalChangeBanner = new();
    private readonly Dictionary<DisplayMode, Button> modeButtons = new();

    private string? currentPath;
    private string? initialPath;
    private string documentNewLine = Environment.NewLine;
    private bool isDirty;
    private bool suppressEditorChange;
    private bool webViewReady;
    private bool darkPreview;
    private DateTime ignoreWatcherUntilUtc;
    private FileSystemWatcher? watcher;
    private CancellationTokenSource? renderDelay;
    private DisplayMode displayMode = DisplayMode.Split;

    public MainForm(string? path)
    {
        initialPath = path;
        Text = "nebula-md — Markdown editor";
        MinimumSize = new Size(1120, 620);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1320, 820);
        BackColor = Ink;
        ForeColor = Color.White;
        Font = new Font("Bahnschrift", 9.5f, FontStyle.Regular);
        AllowDrop = true;
        KeyPreview = true;

        var applicationIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (applicationIcon is not null) Icon = applicationIcon;

        BuildInterface();
        WireEvents();
        ShowWelcomeDocument();
    }

    private void BuildInterface()
    {
        Controls.Add(BuildWorkspace());
        Controls.Add(BuildStatusBar());
        Controls.Add(BuildExternalChangeBanner());
        Controls.Add(BuildHeader());
    }

    private Control BuildHeader()
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 78,
            BackColor = DeepInk,
            Padding = new Padding(22, 13, 18, 11)
        };

        var mark = new Label
        {
            Text = "M",
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Georgia", 18f, FontStyle.Bold),
            ForeColor = DeepInk,
            BackColor = Ochre,
            Size = new Size(48, 48),
            Location = new Point(22, 15)
        };
        header.Controls.Add(mark);

        var brand = new Label
        {
            AutoSize = true,
            Text = "nebula-md",
            Font = new Font("Bahnschrift SemiBold", 15f, FontStyle.Bold),
            ForeColor = Color.FromArgb(240, 238, 228),
            Location = new Point(84, 14)
        };
        header.Controls.Add(brand);

        var subtitle = new Label
        {
            AutoSize = true,
            Text = "MARKDOWN READING DESK",
            Font = new Font("Bahnschrift", 7.5f, FontStyle.Regular),
            ForeColor = Lichen,
            Location = new Point(86, 42)
        };
        header.Controls.Add(subtitle);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 770,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 7, 0, 0)
        };

        var themeButton = CreateToolbarButton("MOON", "Switch preview paper/midnight theme", (_, _) => TogglePreviewTheme(), 70);
        var printButton = CreateToolbarButton("PRINT", "Print the rendered document (Ctrl+P)", async (_, _) => await PrintPreviewAsync(), 66);
        var reloadButton = CreateToolbarButton("RELOAD", "Reload from disk (F5)", (_, _) => ReloadFromDisk(), 76);
        var saveButton = CreateToolbarButton("SAVE", "Save Markdown (Ctrl+S)", (_, _) => SaveDocument(), 62, accent: true);
        var openButton = CreateToolbarButton("OPEN", "Open a Markdown file (Ctrl+O)", (_, _) => ChooseFile(), 62);

        actions.Controls.Add(themeButton);
        actions.Controls.Add(printButton);
        actions.Controls.Add(reloadButton);
        actions.Controls.Add(saveButton);
        actions.Controls.Add(openButton);
        actions.Controls.Add(CreateDivider());

        foreach (var mode in new[] { DisplayMode.Source, DisplayMode.Preview, DisplayMode.Split })
        {
            var modeButton = CreateToolbarButton(mode.ToString().ToUpperInvariant(), $"Show {mode.ToString().ToLowerInvariant()} view", (_, _) => SetDisplayMode(mode), mode == DisplayMode.Preview ? 76 : 68);
            modeButtons[mode] = modeButton;
            actions.Controls.Add(modeButton);
        }

        header.Controls.Add(actions);
        UpdateModeButtons();
        return header;
    }

    private Control BuildWorkspace()
    {
        split.Dock = DockStyle.Fill;
        split.Orientation = Orientation.Vertical;
        split.SplitterWidth = 5;
        // Give the control valid design-time dimensions before applying panel
        // minimums; its WinForms default is only 150px wide.
        split.Size = new Size(1000, 600);
        split.SplitterDistance = 590;
        split.BackColor = Color.FromArgb(61, 63, 57);
        split.Panel1MinSize = 280;
        split.Panel2MinSize = 320;

        var sourcePanel = new Panel { Dock = DockStyle.Fill, BackColor = Ink };
        sourcePanel.Controls.Add(editor);
        sourcePanel.Controls.Add(BuildPaneHeading("SOURCE", "EDITABLE MARKDOWN", Ink));

        editor.Dock = DockStyle.Fill;
        editor.Multiline = true;
        editor.AcceptsTab = true;
        editor.WordWrap = true;
        editor.ScrollBars = ScrollBars.Vertical;
        editor.BorderStyle = BorderStyle.None;
        editor.BackColor = Color.FromArgb(35, 37, 33);
        editor.ForeColor = Color.FromArgb(226, 226, 214);
        editor.Font = new Font("Cascadia Mono", 10.5f, FontStyle.Regular);
        editor.Padding = new Padding(20);

        var previewPanel = new Panel { Dock = DockStyle.Fill, BackColor = Paper };
        previewPanel.Controls.Add(preview);
        previewPanel.Controls.Add(BuildPaneHeading("PREVIEW", "LIVE RENDER", Color.FromArgb(236, 231, 219), darkText: true));
        preview.Dock = DockStyle.Fill;
        preview.DefaultBackgroundColor = Paper;

        split.Panel1.Controls.Add(sourcePanel);
        split.Panel2.Controls.Add(previewPanel);
        return split;
    }

    private Control BuildPaneHeading(string title, string detail, Color background, bool darkText = false)
    {
        var panel = new Panel { Dock = DockStyle.Top, Height = 39, BackColor = background, Padding = new Padding(16, 0, 16, 0) };
        var heading = new Label
        {
            Text = title,
            AutoSize = true,
            Font = new Font("Bahnschrift SemiBold", 8.5f, FontStyle.Bold),
            ForeColor = darkText ? Color.FromArgb(53, 55, 49) : Ochre,
            Location = new Point(17, 13)
        };
        var meta = new Label
        {
            Text = detail,
            AutoSize = true,
            Font = new Font("Bahnschrift", 7.5f),
            ForeColor = darkText ? Color.FromArgb(126, 124, 114) : Muted,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(panel.Width - 112, 13)
        };
        panel.Resize += (_, _) => meta.Left = panel.ClientSize.Width - meta.Width - 17;
        panel.Controls.Add(heading);
        panel.Controls.Add(meta);
        return panel;
    }

    private Control BuildExternalChangeBanner()
    {
        externalChangeBanner.Dock = DockStyle.Top;
        externalChangeBanner.Height = 42;
        externalChangeBanner.BackColor = Color.FromArgb(92, 67, 29);
        externalChangeBanner.Visible = false;

        var text = new Label
        {
            Text = "This file changed on disk while you have unsaved edits.",
            AutoSize = true,
            ForeColor = Color.FromArgb(255, 231, 181),
            Location = new Point(18, 13)
        };
        var reload = new Button
        {
            Text = "RELOAD DISK VERSION",
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(1080, 7),
            Height = 29
        };
        reload.FlatAppearance.BorderColor = Color.FromArgb(185, 139, 57);
        reload.Click += (_, _) => ReloadFromDisk(force: true);
        externalChangeBanner.Resize += (_, _) => reload.Left = externalChangeBanner.ClientSize.Width - reload.Width - 18;
        externalChangeBanner.Controls.Add(text);
        externalChangeBanner.Controls.Add(reload);
        return externalChangeBanner;
    }

    private Control BuildStatusBar()
    {
        var status = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 34,
            BackColor = DeepInk,
            Padding = new Padding(18, 0, 18, 0)
        };
        fileLabel.Text = "UNTITLED.MD";
        fileLabel.AutoSize = true;
        fileLabel.ForeColor = Lichen;
        fileLabel.Font = new Font("Bahnschrift SemiBold", 8f, FontStyle.Bold);
        fileLabel.Location = new Point(18, 10);

        statusLabel.Text = "READY";
        statusLabel.AutoSize = true;
        statusLabel.ForeColor = Muted;
        statusLabel.Font = new Font("Bahnschrift", 8f);
        statusLabel.Location = new Point(190, 10);

        metricsLabel.AutoSize = true;
        metricsLabel.ForeColor = Color.FromArgb(157, 160, 149);
        metricsLabel.Font = new Font("Cascadia Mono", 8f);
        metricsLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        metricsLabel.Location = new Point(status.Width - 240, 9);
        status.Resize += (_, _) => metricsLabel.Left = status.ClientSize.Width - metricsLabel.Width - 18;

        status.Controls.Add(fileLabel);
        status.Controls.Add(statusLabel);
        status.Controls.Add(metricsLabel);
        return status;
    }

    private Button CreateToolbarButton(string text, string tooltip, EventHandler onClick, int width, bool accent = false)
    {
        var button = new Button
        {
            Text = text,
            Width = width,
            Height = 36,
            Margin = new Padding(4, 0, 0, 0),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Font = new Font("Bahnschrift SemiBold", 8f, FontStyle.Bold),
            ForeColor = accent ? DeepInk : Color.FromArgb(221, 222, 211),
            BackColor = accent ? Ochre : Color.FromArgb(41, 43, 39),
            TabStop = false
        };
        button.FlatAppearance.BorderColor = accent ? Ochre : Color.FromArgb(71, 74, 67);
        button.FlatAppearance.MouseOverBackColor = accent ? Color.FromArgb(233, 184, 81) : Color.FromArgb(56, 59, 53);
        button.FlatAppearance.MouseDownBackColor = accent ? Color.FromArgb(198, 145, 42) : Color.FromArgb(31, 33, 29);
        button.Click += onClick;
        new ToolTip { InitialDelay = 350, ReshowDelay = 100 }.SetToolTip(button, tooltip);
        return button;
    }

    private static Control CreateDivider() => new Panel
    {
        Width = 1,
        Height = 30,
        BackColor = Color.FromArgb(67, 69, 63),
        Margin = new Padding(12, 3, 9, 0)
    };

    private void WireEvents()
    {
        Shown += async (_, _) => await InitializePreviewAsync();
        editor.TextChanged += EditorOnTextChanged;
        FormClosing += MainFormOnFormClosing;
        DragEnter += (_, e) => e.Effect = HasMarkdownFile(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        DragDrop += (_, e) =>
        {
            var path = GetDroppedMarkdownFile(e.Data);
            if (path is not null && ConfirmDiscardChanges()) OpenFile(path);
        };
    }

    private async Task InitializePreviewAsync()
    {
        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "nebula-md",
                "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
            await preview.EnsureCoreWebView2Async(environment);
            preview.CoreWebView2.Settings.AreDevToolsEnabled = false;
            preview.CoreWebView2.Settings.IsStatusBarEnabled = false;
            preview.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;
            preview.CoreWebView2.NavigationStarting += PreviewOnNavigationStarting;
            preview.CoreWebView2.NewWindowRequested += PreviewOnNewWindowRequested;
            webViewReady = true;

            if (!string.IsNullOrWhiteSpace(initialPath) && File.Exists(initialPath))
            {
                OpenFile(Path.GetFullPath(initialPath));
                initialPath = null;
            }
            else
            {
                RenderPreview();
            }
        }
        catch (WebView2RuntimeNotFoundException)
        {
            statusLabel.Text = "WEBVIEW2 RUNTIME REQUIRED";
            MessageBox.Show(
                "nebula-md needs the Microsoft Edge WebView2 Runtime to display previews. Install the Evergreen WebView2 Runtime, then reopen the app.",
                "Preview runtime missing",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    private void ShowWelcomeDocument()
    {
        suppressEditorChange = true;
        editor.Text = """
# Welcome to nebula-md

Open a `.md` file or drop one anywhere on this window. The preview updates as you type.

## A quick formatting check

- **Bold**, *italic*, and `inline code`
- [Links](https://commonmark.org) open in your browser
- Tables, task lists, footnotes, and fenced code are supported

> A quiet place to read what you wrote—not the syntax you used.

```csharp
Console.WriteLine("Hello, Markdown.");
```

| Shortcut | Action |
|:--|:--|
| `Ctrl+O` | Open file |
| `Ctrl+S` | Save file |
| `F5` | Reload from disk |
| `Ctrl+P` | Print preview |
""";
        editor.SelectionStart = 0;
        editor.SelectionLength = 0;
        suppressEditorChange = false;
        isDirty = false;
        UpdateDocumentStatus();
    }

    private void EditorOnTextChanged(object? sender, EventArgs e)
    {
        if (!suppressEditorChange) isDirty = true;
        UpdateDocumentStatus();
        QueuePreviewRender();
    }

    private async void QueuePreviewRender()
    {
        renderDelay?.Cancel();
        renderDelay?.Dispose();
        renderDelay = new CancellationTokenSource();
        try
        {
            await Task.Delay(160, renderDelay.Token);
            RenderPreview();
        }
        catch (OperationCanceledException)
        {
            // A newer edit superseded this render.
        }
    }

    private void RenderPreview()
    {
        if (!webViewReady) return;

        try
        {
            var baseFolder = currentPath is null
                ? Environment.CurrentDirectory
                : Path.GetDirectoryName(currentPath)!;
            preview.CoreWebView2.ClearVirtualHostNameToFolderMapping("markdown.local");
            preview.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "markdown.local",
                baseFolder,
                CoreWebView2HostResourceAccessKind.Allow);

            var rendered = Markdown.ToHtml(editor.Text, pipeline);
            var title = WebUtility.HtmlEncode(currentPath is null ? "Untitled" : Path.GetFileNameWithoutExtension(currentPath));
            preview.NavigateToString(BuildHtmlDocument(title, rendered));
            statusLabel.Text = isDirty ? "EDITING" : "UP TO DATE";
        }
        catch (Exception ex)
        {
            statusLabel.Text = "PREVIEW ERROR";
            Debug.WriteLine(ex);
        }
    }

    private string BuildHtmlDocument(string title, string content)
    {
        var themeClass = darkPreview ? "night" : "paper";
        return $$"""
<!doctype html>
<html lang="en" class="{{themeClass}}">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<base href="https://markdown.local/">
<title>{{title}}</title>
<style>
:root { color-scheme: light; --page:#f6f1e6; --ink:#2a2d28; --soft:#6f7169; --line:#d9d1c2; --accent:#a86619; --code:#ebe4d8; --quote:#e7edde; }
.night { color-scheme:dark; --page:#20221f; --ink:#e6e4db; --soft:#a4a79d; --line:#44473f; --accent:#e1ad4d; --code:#2b2e29; --quote:#293127; }
* { box-sizing:border-box; }
html { background:var(--page); }
body { margin:0; color:var(--ink); background:
  radial-gradient(circle at 12% 8%, rgba(186,139,58,.08), transparent 28rem),
  linear-gradient(90deg, transparent 39px, rgba(140,110,62,.09) 40px, transparent 41px), var(--page);
  font-family:Charter,"Iowan Old Style","Palatino Linotype",Georgia,serif; font-size:18px; line-height:1.72; }
main { max-width:860px; margin:0 auto; padding:68px 68px 120px 86px; }
h1,h2,h3,h4,h5,h6 { font-family:"Bahnschrift SemiBold","Franklin Gothic Medium",sans-serif; line-height:1.16; letter-spacing:-.025em; margin:1.8em 0 .58em; }
h1 { font-size:2.65em; margin-top:0; padding-bottom:.34em; border-bottom:1px solid var(--line); }
h2 { font-size:1.75em; }
h3 { font-size:1.3em; color:var(--accent); }
p { margin:0 0 1.15em; }
a { color:var(--accent); text-decoration-thickness:1px; text-underline-offset:3px; }
a:hover { text-decoration-thickness:2px; }
blockquote { margin:1.7em 0; padding:.35em 1.35em; color:var(--soft); background:var(--quote); border-left:4px solid var(--accent); }
blockquote p:last-child { margin-bottom:0; }
code,kbd { font-family:"Cascadia Mono",Consolas,monospace; font-size:.82em; background:var(--code); border:1px solid var(--line); border-radius:4px; padding:.12em .35em; }
pre { position:relative; overflow:auto; margin:1.5em 0; padding:1.2em 1.35em; background:var(--code); border:1px solid var(--line); border-radius:7px; line-height:1.55; box-shadow:inset 3px 0 0 var(--accent); }
pre code { padding:0; border:0; background:transparent; }
table { width:100%; border-collapse:collapse; margin:1.6em 0; font-size:.93em; }
th { font-family:"Bahnschrift SemiBold",sans-serif; text-align:left; color:var(--accent); background:var(--code); }
th,td { padding:.7em .85em; border:1px solid var(--line); }
tr:nth-child(even) td { background:color-mix(in srgb, var(--code) 52%, transparent); }
hr { border:0; border-top:1px solid var(--line); margin:2.5em 0; }
img { max-width:100%; height:auto; border-radius:5px; box-shadow:0 10px 30px rgba(30,25,18,.14); }
ul,ol { padding-left:1.5em; }
li { padding-left:.18em; margin:.25em 0; }
input[type=checkbox] { width:1.05em; height:1.05em; accent-color:var(--accent); vertical-align:-.12em; }
.footnotes { margin-top:3em; padding-top:1em; border-top:1px solid var(--line); color:var(--soft); font-size:.88em; }
@media(max-width:700px) { body{font-size:16px} main{padding:42px 28px 80px 44px} h1{font-size:2.1em} }
@media print { body{background:white;color:#222} main{max-width:none;padding:0} a{color:inherit} }
</style>
</head>
<body>
<main>{{content}}</main>
<script>
document.addEventListener('click', function (event) {
  const link = event.target.closest('a');
  if (!link) return;
  const raw = link.getAttribute('href') || '';
  if (raw.startsWith('#')) {
    event.preventDefault();
    const target = document.getElementById(decodeURIComponent(raw.slice(1)));
    if (target) target.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }
});
</script>
</body>
</html>
""";
    }

    private void ChooseFile()
    {
        if (!ConfirmDiscardChanges()) return;
        using var dialog = new OpenFileDialog
        {
            Title = "Open Markdown",
            Filter = "Markdown files (*.md;*.markdown;*.mdown)|*.md;*.markdown;*.mdown|Text files (*.txt)|*.txt|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == DialogResult.OK) OpenFile(dialog.FileName);
    }

    private void OpenFile(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            var fileText = File.ReadAllText(fullPath);
            documentNewLine = DetectNewLine(fileText);
            suppressEditorChange = true;
            editor.Text = NormalizeForWindowsEditor(fileText);
            editor.SelectionStart = 0;
            editor.SelectionLength = 0;
            currentPath = fullPath;
            isDirty = false;
            externalChangeBanner.Visible = false;
            ConfigureWatcher();
            UpdateDocumentStatus();
            RenderPreview();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open the file.\n\n{ex.Message}", "Open failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            suppressEditorChange = false;
        }
    }

    private bool SaveDocument()
    {
        if (currentPath is null)
        {
            using var dialog = new SaveFileDialog
            {
                Title = "Save Markdown",
                Filter = "Markdown file (*.md)|*.md|Markdown file (*.markdown)|*.markdown|Text file (*.txt)|*.txt",
                DefaultExt = "md",
                AddExtension = true,
                FileName = "document.md"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return false;
            currentPath = dialog.FileName;
        }

        try
        {
            ignoreWatcherUntilUtc = DateTime.UtcNow.AddSeconds(1.5);
            var outputText = ConvertToDocumentNewLines(editor.Text, documentNewLine);
            File.WriteAllText(currentPath, outputText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            isDirty = false;
            externalChangeBanner.Visible = false;
            ConfigureWatcher();
            UpdateDocumentStatus();
            statusLabel.Text = "SAVED";
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not save the file.\n\n{ex.Message}", "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private void ReloadFromDisk(bool force = false)
    {
        if (currentPath is null || !File.Exists(currentPath)) return;
        if (!force && isDirty)
        {
            var choice = MessageBox.Show(this, "Reloading will discard your unsaved edits. Continue?", "Reload from disk", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (choice != DialogResult.Yes) return;
        }
        OpenFile(currentPath);
        statusLabel.Text = "RELOADED";
    }

    private void ConfigureWatcher()
    {
        watcher?.Dispose();
        watcher = null;
        if (currentPath is null) return;

        watcher = new FileSystemWatcher(Path.GetDirectoryName(currentPath)!, Path.GetFileName(currentPath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true
        };
        watcher.Changed += FileChangedOutsideApp;
        watcher.Renamed += FileChangedOutsideApp;
        watcher.Deleted += FileChangedOutsideApp;
    }

    private void FileChangedOutsideApp(object sender, FileSystemEventArgs e)
    {
        if (DateTime.UtcNow < ignoreWatcherUntilUtc || IsDisposed) return;
        BeginInvoke(async () =>
        {
            await Task.Delay(180);
            if (isDirty)
            {
                externalChangeBanner.Visible = true;
                statusLabel.Text = "CHANGED ON DISK";
            }
            else if (currentPath is not null && File.Exists(currentPath))
            {
                OpenFile(currentPath);
                statusLabel.Text = "SYNCED FROM DISK";
            }
        });
    }

    private void SetDisplayMode(DisplayMode mode)
    {
        displayMode = mode;
        // Expand both panes first so switching directly between source-only and
        // preview-only never asks SplitContainer to collapse both panels.
        split.Panel1Collapsed = false;
        split.Panel2Collapsed = false;
        split.Panel1Collapsed = mode == DisplayMode.Preview;
        split.Panel2Collapsed = mode == DisplayMode.Source;
        UpdateModeButtons();
    }

    private void UpdateModeButtons()
    {
        foreach (var (mode, button) in modeButtons)
        {
            var selected = mode == displayMode;
            button.BackColor = selected ? Color.FromArgb(80, 86, 73) : Color.FromArgb(41, 43, 39);
            button.ForeColor = selected ? Color.White : Color.FromArgb(180, 182, 172);
            button.FlatAppearance.BorderColor = selected ? Lichen : Color.FromArgb(71, 74, 67);
        }
    }

    private void TogglePreviewTheme()
    {
        darkPreview = !darkPreview;
        preview.DefaultBackgroundColor = darkPreview ? Color.FromArgb(32, 34, 31) : Paper;
        RenderPreview();
        statusLabel.Text = darkPreview ? "MIDNIGHT PREVIEW" : "PAPER PREVIEW";
    }

    private Task PrintPreviewAsync()
    {
        if (webViewReady)
        {
            preview.CoreWebView2.ShowPrintUI(CoreWebView2PrintDialogKind.Browser);
        }
        return Task.CompletedTask;
    }

    private void PreviewOnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (e.Uri == "about:blank") return;
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri)) return;

        if (uri.Host.Equals("markdown.local", StringComparison.OrdinalIgnoreCase))
        {
            var extension = Path.GetExtension(uri.AbsolutePath);
            if (extension.Equals(".md", StringComparison.OrdinalIgnoreCase) || extension.Equals(".markdown", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                var folder = currentPath is null ? Environment.CurrentDirectory : Path.GetDirectoryName(currentPath)!;
                var relative = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')).Replace('/', Path.DirectorySeparatorChar);
                var target = Path.GetFullPath(Path.Combine(folder, relative));
                if (File.Exists(target) && ConfirmDiscardChanges()) OpenFile(target);
            }
            return;
        }

        if (uri.Scheme is "http" or "https" or "mailto" or "file")
        {
            e.Cancel = true;
            OpenExternal(uri.ToString());
        }
    }

    private void PreviewOnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        OpenExternal(e.Uri);
    }

    private static void OpenExternal(string uri)
    {
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch { /* The status bar remains usable if Windows has no handler. */ }
    }

    private void UpdateDocumentStatus()
    {
        var name = currentPath is null ? "UNTITLED.MD" : Path.GetFileName(currentPath).ToUpperInvariant();
        fileLabel.Text = isDirty ? $"{name}  •" : name;
        var words = Regex.Matches(editor.Text, @"[\p{L}\p{N}]+(?:['’\-][\p{L}\p{N}]+)*").Count;
        var lines = editor.TextLength == 0 ? 0 : editor.Lines.Length;
        metricsLabel.Text = $"{words:N0} WORDS   {lines:N0} LINES   UTF-8";
        Text = $"{(isDirty ? "• " : string.Empty)}{(currentPath is null ? "Untitled" : Path.GetFileName(currentPath))} — nebula-md";
    }

    private bool ConfirmDiscardChanges()
    {
        if (!isDirty) return true;
        var choice = MessageBox.Show(this, "Save your changes before opening another file?", "Unsaved changes", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        return choice switch
        {
            DialogResult.Yes => SaveDocument(),
            DialogResult.No => true,
            _ => false
        };
    }

    private void MainFormOnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!ConfirmDiscardChanges()) e.Cancel = true;
        if (!e.Cancel)
        {
            watcher?.Dispose();
            renderDelay?.Cancel();
            renderDelay?.Dispose();
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.O:
                ChooseFile();
                return true;
            case Keys.Control | Keys.S:
                SaveDocument();
                return true;
            case Keys.Control | Keys.P:
                _ = PrintPreviewAsync();
                return true;
            case Keys.F5:
                ReloadFromDisk();
                return true;
            case Keys.Control | Keys.D1:
                SetDisplayMode(DisplayMode.Source);
                return true;
            case Keys.Control | Keys.D2:
                SetDisplayMode(DisplayMode.Split);
                return true;
            case Keys.Control | Keys.D3:
                SetDisplayMode(DisplayMode.Preview);
                return true;
            default:
                return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    private static bool HasMarkdownFile(IDataObject? data) => GetDroppedMarkdownFile(data) is not null;

    private static string DetectNewLine(string text)
    {
        if (text.Contains("\r\n", StringComparison.Ordinal)) return "\r\n";
        if (text.Contains('\n')) return "\n";
        if (text.Contains('\r')) return "\r";
        return Environment.NewLine;
    }

    private static string NormalizeForWindowsEditor(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("\n", Environment.NewLine, StringComparison.Ordinal);

    private static string ConvertToDocumentNewLines(string text, string newLine)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        return newLine == "\n" ? normalized : normalized.Replace("\n", newLine, StringComparison.Ordinal);
    }

    private static string? GetDroppedMarkdownFile(IDataObject? data)
    {
        if (data?.GetData(DataFormats.FileDrop) is not string[] files) return null;
        return files.FirstOrDefault(path =>
        {
            var extension = Path.GetExtension(path);
            return extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".markdown", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".mdown", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".txt", StringComparison.OrdinalIgnoreCase);
        });
    }

    private enum DisplayMode
    {
        Source,
        Split,
        Preview
    }
}
