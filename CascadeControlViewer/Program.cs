using System;
using System.Linq;
using System.Windows.Forms;

namespace CascadeControlViewer
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
            Application.Run(new TreeViewer(args.Length > 0 ? args[0] : null, isReadonly: !args.Contains("-edit")));
        }
    }
}