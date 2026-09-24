using System;
using System.Windows.Forms;

namespace Quickfire.Tray
{
    internal static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length != 0 && (args.Length != 2 || args[0] != "--credential-directory")) return;
            Application.Run(new SystemTray(args.Length == 2 ? args[1] : null));
        }
    }
}
