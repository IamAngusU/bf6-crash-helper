using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: AssemblyTitle("BF6 Crash Helper")]
[assembly: AssemblyDescription("BF6 / REDSEC opt-in reversible Rapid Royale workaround")]
[assembly: AssemblyCompany("IamAngusU")]
[assembly: AssemblyProduct("BF6 Crash Helper")]
[assembly: AssemblyCopyright("Copyright © 2026 IamAngusU")]
[assembly: AssemblyVersion("0.4.0.0")]

namespace Bf6CrashHelper
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            UiLanguage.Set(false);
            if (MirakWorker.HandleWorker(args)) return;
            if (args.Length != 0) { MessageBox.Show("Start BF6 Crash Helper without command-line arguments."); return; }
            bool first;
            using (var mutex = new Mutex(true, "Local\\BF6CrashHelper", out first))
            {
                if (!first) { MessageBox.Show("BF6 Crash Helper is already open."); return; }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
        }
    }

}
