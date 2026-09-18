using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.VisualBasic.ApplicationServices;

internal static class SingleInstanceChecks
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private sealed record Snapshot(int Pid, string[] Paths, string? DirtyText, FormWindowState WindowState);

    public static int RunHost(string[] args)
    {
        Application.EnableVisualStyles();
        var folder = args[1];
        var type = Assembly.Load("nebula-md").GetType("NebulaMd.SingleInstanceApplication", true)!;
        var application = (WindowsFormsApplicationBase)Activator.CreateInstance(type)!;
        using var timer = new System.Windows.Forms.Timer { Interval = 50 };
        var edited = false;
        var minimized = false;
        application.Startup += (_, _) =>
        {
            type.GetMethod("OnCreateMainForm", PrivateInstance)!.Invoke(application, null);
            var form = application.ApplicationContext.MainForm!;
            form.Opacity = 0;
            form.ShowInTaskbar = false;
            timer.Start();
        };
        timer.Tick += (_, _) =>
        {
            var form = application.ApplicationContext.MainForm!;
            var documents = ((IEnumerable)form.GetType().GetField("documents", PrivateInstance)!.GetValue(form)!).Cast<object>().ToArray();
            if (File.Exists(Path.Combine(folder, "edit")) && !edited)
            {
                ((TextBox)form.GetType().GetField("editor", PrivateInstance)!.GetValue(form)!).Text = "# Unsaved instance test";
                edited = true;
            }
            form.Enabled = !File.Exists(Path.Combine(folder, "block"));
            if (File.Exists(Path.Combine(folder, "minimize")) && !minimized)
            {
                form.WindowState = FormWindowState.Minimized;
                minimized = true;
            }
            var dirty = documents.FirstOrDefault(document => Get<bool>(document, "IsDirty"));
            var snapshot = new Snapshot(Environment.ProcessId,
                documents.Select(document => Get<string?>(document, "Path")).OfType<string>().ToArray(),
                dirty is null ? null : Get<string>(dirty, "Text"), form.WindowState);
            var target = Path.Combine(folder, $"instance-{Environment.ProcessId}.json");
            File.WriteAllText(target + ".tmp", JsonSerializer.Serialize(snapshot));
            File.Move(target + ".tmp", target, overwrite: true);
            if (File.Exists(Path.Combine(folder, "stop")))
            {
                timer.Stop();
                // Only these temporary test documents are discarded on shutdown.
                foreach (var document in documents) document.GetType().GetProperty("IsDirty")!.SetValue(document, false);
                form.Close();
            }
        };
        // Calling Run from the test assembly gives these processes their own
        // instance identity, isolated from the user's installed app.
        application.Run(args.Skip(2).Select(Path.GetFullPath).ToArray());
        return 0;
    }

    public static void Run(string parentFolder, Action<bool, string> check)
    {
        var folder = Path.Combine(parentFolder, "instance checks");
        Directory.CreateDirectory(folder);
        var filenames = new[] { "One.md", "Two with spaces.md", "Résumé.mdown", "Four.md" };
        foreach (var filename in filenames) File.WriteAllText(Path.Combine(folder, filename), "# " + filename);
        var processes = new List<Process>();
        Process Launch(params string[] paths)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = folder
            };
            start.ArgumentList.Add("--instance-host");
            start.ArgumentList.Add(folder);
            foreach (var path in paths) start.ArgumentList.Add(path);
            var process = Process.Start(start)!;
            processes.Add(process);
            return process;
        }
        Snapshot? ReadSnapshot()
        {
            var snapshots = Directory.GetFiles(folder, "instance-*.json");
            checkSingleSnapshot(snapshots);
            if (snapshots.Length == 0) return null;
            try
            {
                // The host replaces this file atomically on every timer tick.
                // Allow that replacement while a snapshot is being read so the
                // test reader cannot crash the application with access denied.
                using var stream = new FileStream(snapshots[0], FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                return JsonSerializer.Deserialize<Snapshot>(stream);
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }
        static void checkSingleSnapshot(string[] files)
        {
            if (files.Length > 1) throw new InvalidOperationException("Multiple application windows were created.");
        }
        try
        {
            // Explorer-style simultaneous cold launches: one file per process.
            foreach (var filename in filenames.Take(3)) Launch(filename);
            WaitUntil(() => ReadSnapshot()?.Paths.Length == 3 && processes.Count(process => !process.HasExited) == 1);
            check(true, "Simultaneous Explorer launches open three documents in one process");
            check(ReadSnapshot()!.Paths.All(Path.IsPathFullyQualified), "Forwarded paths preserve spaces, Unicode, and the launching folder");

            File.WriteAllText(Path.Combine(folder, "edit"), "");
            File.WriteAllText(Path.Combine(folder, "block"), "");
            WaitUntil(() => ReadSnapshot()?.DirtyText == "# Unsaved instance test");
            var later = Launch(filenames[3]);
            WaitUntil(() => later.HasExited);
            Thread.Sleep(400);
            check(ReadSnapshot()!.Paths.Length == 3, "Incoming files wait while the main window is disabled by a dialog");
            File.Delete(Path.Combine(folder, "block"));
            WaitUntil(() => ReadSnapshot()?.Paths.Length == 4);
            check(ReadSnapshot()!.DirtyText == "# Unsaved instance test", "Later launches preserve unsaved edits in existing tabs");

            var duplicate = Launch(filenames[0].ToUpperInvariant());
            WaitUntil(() => duplicate.HasExited);
            Thread.Sleep(300);
            check(ReadSnapshot()!.Paths.Length == 4, "Repeated Explorer opens reuse an existing tab");

            File.WriteAllText(Path.Combine(folder, "minimize"), "");
            WaitUntil(() => ReadSnapshot()?.WindowState == FormWindowState.Minimized);
            var empty = Launch();
            WaitUntil(() => empty.HasExited && ReadSnapshot()?.WindowState == FormWindowState.Normal);
            check(ReadSnapshot()!.Paths.Length == 4, "Launching without files restores the existing window");
        }
        catch
        {
            foreach (var snapshot in Directory.GetFiles(folder, "instance-*.json")) Console.Error.WriteLine(File.ReadAllText(snapshot));
            foreach (var process in processes) Console.Error.WriteLine($"Process {process.Id}: {(process.HasExited ? $"exit {process.ExitCode}" : "running")}");
            throw;
        }
        finally
        {
            File.WriteAllText(Path.Combine(folder, "stop"), "");
            foreach (var process in processes)
            {
                if (!process.WaitForExit(10000)) process.Kill();
                process.Dispose();
            }
        }
    }

    private static T Get<T>(object instance, string name) => (T)instance.GetType().GetProperty(name)!.GetValue(instance)!;
    private static void WaitUntil(Func<bool> predicate)
    {
        var watch = Stopwatch.StartNew();
        while (!predicate() && watch.Elapsed < TimeSpan.FromSeconds(20)) Thread.Sleep(30);
        if (!predicate()) throw new TimeoutException("Single-instance handoff did not complete.");
    }
}
