using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace NXInput_Server
{
    internal static class Program
    {
        /// <summary>
        /// Punto di ingresso principale dell'applicazione.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--apply-update") { Environment.ExitCode = ReleaseUpdater.ApplyUpdate(args); return; }
            using (var instance = new System.Threading.Mutex(false, @"Local\Yukinsnow.LiteLink.Receiver"))
            {
                bool owns;
                try { owns = instance.WaitOne(0, false); }
                catch (System.Threading.AbandonedMutexException) { owns = true; }
                if (!owns) return;
                try {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.Run(new Form1());
                }
                finally { instance.ReleaseMutex(); }
            }
        }
    }
}
