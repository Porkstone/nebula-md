using Microsoft.VisualBasic.ApplicationServices;

namespace NebulaMd;

internal sealed class SingleInstanceApplication : WindowsFormsApplicationBase
{
    private readonly Queue<string> pendingPaths = new();
    private readonly object pendingPathsLock = new();
    private readonly System.Windows.Forms.Timer openTimer = new() { Interval = 100 };
    private bool openingFiles;

    public SingleInstanceApplication()
    {
        // The pipe listener starts before the main form exists. Capture a UI
        // context now so simultaneous cold launches wait for the message loop.
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        IsSingleInstance = true;
        ShutdownStyle = ShutdownMode.AfterMainFormCloses;
        openTimer.Tick += (_, _) => OpenPendingFiles();
    }

    protected override void OnCreateMainForm()
    {
        var form = new MainForm(CommandLineArgs.ToArray());
        form.HandleCreated += (_, _) =>
        {
            lock (pendingPathsLock)
            {
                if (pendingPaths.Count > 0) openTimer.Start();
            }
        };
        MainForm = form;
    }

    protected override void OnStartupNextInstance(StartupNextInstanceEventArgs eventArgs)
    {
        lock (pendingPathsLock)
        {
            foreach (var path in eventArgs.CommandLine) pendingPaths.Enqueue(path);
        }
        if (MainForm is MainForm { IsHandleCreated: true, IsDisposed: false } form)
        {
            form.BeginInvoke(() => openTimer.Start());
        }
        eventArgs.BringToForeground = true;
        base.OnStartupNextInstance(eventArgs);
    }

    private void OpenPendingFiles()
    {
        // Modal save/close dialogs disable the form. Wait until they finish so
        // incoming files cannot change the active document beneath a save prompt.
        if (openingFiles || MainForm is not MainForm { Enabled: true, IsDisposed: false } form) return;
        openingFiles = true;
        try
        {
            while (true)
            {
                string? path;
                lock (pendingPathsLock)
                {
                    if (!pendingPaths.TryDequeue(out path))
                    {
                        openTimer.Stop();
                        break;
                    }
                }
                form.OpenFile(path);
            }
        }
        finally { openingFiles = false; }
    }

    protected override void OnShutdown()
    {
        openTimer.Dispose();
        base.OnShutdown();
    }
}
