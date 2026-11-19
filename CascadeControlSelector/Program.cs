using System;
using System.Windows.Forms;

namespace CascadeControlSelector
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
            Application.Run(new TreeSelector(args.Length > 0 ? args[0] : null));
        }
    }
}