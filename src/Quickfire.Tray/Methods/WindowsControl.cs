using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Quickfire.Shared.Bridge;

namespace Quickfire.Tray
{
    public static class WindowsControl
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr window, int command);

        public static void BringWindowToFront(IntPtr handle)
        {
            if (handle == IntPtr.Zero) return;
            ShowWindow(handle, 9);
            SetForegroundWindow(handle);
        }

        public static void PerformWindowsFunction(string command, List<string> parameters)
        {
            if (parameters == null || parameters.Count != 1) throw new ArgumentException("A single local path is required.");
            switch (command)
            {
                case "Windows_OpenFolder": OpenFolder(parameters[0]); break;
                case "Windows_OpenFile": OpenFile(parameters[0]); break;
                default: throw new NotSupportedException("Unsupported Windows command.");
            }
        }

        public static void OpenFolder(string path)
        {
            path = NativeBridgeSafety.ValidatePath(path, false);
            var selectFile = File.Exists(path);
            if (!Directory.Exists(selectFile ? Path.GetDirectoryName(path) : path))
                throw new DirectoryNotFoundException("The requested folder is unavailable.");
            RejectReparsePoints(path);
            // Explicit executable; the path is one quoted argument and is never evaluated by a shell.
            Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"))
            {
                Arguments = (selectFile ? "/select," : "") + QuotePath(path),
                UseShellExecute = false
            });
        }

        private static string QuotePath(string path)
        {
            var trailing = path.Length - path.TrimEnd('\\').Length;
            return "\"" + path + new string('\\', trailing) + "\"";
        }

        public static void OpenFile(string path)
        {
            path = NativeBridgeSafety.ValidatePath(path, true);
            if (!File.Exists(path)) throw new FileNotFoundException("The requested document is unavailable.");
            RejectReparsePoints(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }

        private static void RejectReparsePoints(string path)
        {
            for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            {
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new ArgumentException("Linked filesystem paths cannot be opened by the bridge.");
            }
        }
    }
}
