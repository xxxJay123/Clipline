using System.Collections.Specialized;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Clipline;

/// <summary>What a photo can do with its file, done the way Windows itself does it.</summary>
internal static class ShellOps
{
    public static void Open(string path) => Start(new ProcessStartInfo(path) { UseShellExecute = true });

    /// <summary>
    /// Press and hold: open the picture in Paint, the editor every Windows
    /// has. Saving there writes over the file, and the photo on the line
    /// updates by itself. Recordings open in their usual app.
    /// </summary>
    public static void Edit(string path)
    {
        if (MediaKind.IsRecording(path))
        {
            Open(path);
            return;
        }
        if (Start(new ProcessStartInfo("mspaint.exe") { UseShellExecute = true, ArgumentList = { path } })) return;
        Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "edit" });
    }

    /// <summary>Opens Explorer on the folder with the file selected, reusing a window already open there.</summary>
    public static void ShowInExplorer(string path)
    {
        var pidl = Native.ILCreateFromPath(path);
        if (pidl != 0)
        {
            try
            {
                if (Native.SHOpenFolderAndSelectItems(pidl, 0, null, 0) == 0) return;
            }
            finally
            {
                Native.ILFree(pidl);
            }
        }
        Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true, ArgumentList = { "/select," + path } });
    }

    public static void OpenFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }

    /// <summary>Sends a file to the Recycle Bin, never deleting it outright and never asking.</summary>
    public static bool Recycle(string path)
    {
        if (!File.Exists(path)) return true;
        var op = new Native.SHFILEOPSTRUCT
        {
            wFunc = Native.FO_DELETE,
            pFrom = path + "\0\0",
            fFlags = (ushort)(Native.FOF_ALLOWUNDO | Native.FOF_NOCONFIRMATION | Native.FOF_SILENT | Native.FOF_NOERRORUI),
        };
        return Native.SHFileOperation(ref op) == 0 && !op.fAnyOperationsAborted && !File.Exists(path);
    }

    /// <summary>
    /// Copies a photo the way apps expect to paste it: the picture itself for
    /// images (PNG and bitmap), and the file, which is how a recording pastes.
    /// Marked as Clipline's own so the clipboard watcher leaves it alone.
    /// </summary>
    public static bool Copy(string path, BitmapSource? image)
    {
        var data = new DataObject();
        data.SetData(ClipboardPolicy.SelfFormat, "1");
        data.SetFileDropList(new StringCollection { path });
        if (image != null)
        {
            data.SetImage(image);
            using var png = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            encoder.Save(png);
            data.SetData("PNG", new MemoryStream(png.ToArray()), false);
        }
        for (var attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(data, true);
                return true;
            }
            catch (COMException)
            {
                // Another app has the clipboard open for a moment.
                Thread.Sleep(40);
            }
        }
        return false;
    }

    /// <summary>The full picture, for copying. Recordings are copied as files only.</summary>
    public static BitmapSource? FullImage(string path)
    {
        if (!MediaKind.IsImage(path)) return null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            frame.Freeze();
            return frame;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException
            or FileFormatException or COMException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    public static string Desktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    /// <summary>Moves a file into a folder under a name that is free there. Returns the new path.</summary>
    public static string? MoveTo(string path, string folder)
    {
        try
        {
            var target = Inbox.UniquePath(folder, Path.GetFileName(path));
            File.Move(path, target);
            return target;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Beep() => System.Media.SystemSounds.Beep.Play();

    static bool Start(ProcessStartInfo info)
    {
        try
        {
            using var _ = Process.Start(info);
            return true;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            return false;
        }
    }
}

/// <summary>Launching with Windows, through the per-user Run key: no installer, no admin.</summary>
internal static class Startup
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "Clipline";

    static string Command => $"\"{Environment.ProcessPath}\" --background";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(Name) is string value && value.Equals(Command, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void Set(bool on)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) key.SetValue(Name, Command);
        else key.DeleteValue(Name, false);
    }
}
