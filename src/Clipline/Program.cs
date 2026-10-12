namespace Clipline;

internal static class Program
{
    const string InstanceName = @"Local\Clipline.Instance";
    const string ShowSignalName = @"Local\Clipline.Show";

    [STAThread]
    static int Main(string[] args)
    {
        if (Arg(args, "--snapshot") is { } snapshotFolder) return Snapshot.Run(snapshotFolder);

        var demo = args.Contains("--demo");
        var dataFolder = demo
            ? Path.Combine(Path.GetTempPath(), "Clipline Demo")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Clipline");

        // One line is enough. Launching again shows the one already running.
        using var instance = new Mutex(true, demo ? InstanceName + ".Demo" : InstanceName, out var first);
        using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, demo ? ShowSignalName + ".Demo" : ShowSignalName);
        if (!first)
        {
            showSignal.Set();
            return 0;
        }

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        // A line that hangs around all day should not vanish over one bad file: note it and carry on.
        var log = Path.Combine(dataFolder, "errors.log");
        app.DispatcherUnhandledException += (_, e) =>
        {
            Log(log, e.Exception);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log(log, e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log(log, e.Exception);
            e.SetObserved();
        };
        var controller = new AppController(new Options(dataFolder, demo, args.Contains("--background")));
        app.Startup += async (_, _) =>
        {
            controller.Start();
            if (demo) await controller.HangAsync(Samples.Write(Path.Combine(dataFolder, "Samples")));
        };
        app.Exit += (_, _) => controller.Dispose();

        var listener = new Thread(() =>
        {
            while (showSignal.WaitOne())
                app.Dispatcher.BeginInvoke(controller.ShowFromOutside);
        }) { IsBackground = true, Name = "Clipline show signal" };
        listener.Start();

        return app.Run();
    }

    static void Log(string path, Exception? e)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024) File.Delete(path);
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {e}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    static string? Arg(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
