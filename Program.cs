using System;
using System.Windows.Forms;
using Test_1.UI;

namespace Test_1
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) => {
                try { System.IO.File.WriteAllText("crash.log", e.ExceptionObject?.ToString() ?? "Unknown domain error"); } catch { }
            };
            Application.ThreadException += (s, e) => {
                try { System.IO.File.WriteAllText("crash.log", e.Exception?.ToString() ?? "Unknown thread error"); } catch { }
            };
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                try { System.IO.File.WriteAllText("startup_error.log", ex.ToString()); } catch { }
                MessageBox.Show("Startup error: " + ex.ToString(), "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}