namespace NebulaMd;

internal sealed class OpenDocument : IDisposable
{
    public string? Path { get; set; }
    public string Text { get; set; } = string.Empty;
    public string NewLine { get; set; } = Environment.NewLine;
    public bool IsDirty { get; set; }
    public bool ChangedOnDisk { get; set; }
    public int SelectionStart { get; set; }
    public int SelectionLength { get; set; }
    public FileSystemWatcher? Watcher { get; set; }
    public int ChangeVersion { get; set; }
    public string Name => Path is null ? "Untitled.md" : System.IO.Path.GetFileName(Path);
    public string Caption => Name + (IsDirty ? " •" : string.Empty);

    public override string ToString() => Caption;
    public void Dispose() => Watcher?.Dispose();
}
