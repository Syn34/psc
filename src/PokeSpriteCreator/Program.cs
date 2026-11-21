using System;
using System.IO;
using System.Windows.Forms;
using PokeSpriteCreator.Model;
using PokeSpriteCreator.UI;

namespace PokeSpriteCreator
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length >= 1 && args[0] == "--selftest") return SelfTest.Run(args.Length > 1 ? args[1] : null);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) => Crash(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Crash(e.ExceptionObject as Exception);
            var settings = Settings.Load();
            string file = args.Length > 0 && File.Exists(args[0]) ? args[0] : null;
            Application.Run(new MainForm(settings, file));
            return 0;
        }

        static void Crash(Exception ex)
        {
            try
            {
                Directory.CreateDirectory(Settings.Dir);
                File.AppendAllText(Path.Combine(Settings.Dir, "errorlog.txt"), DateTime.Now + "\r\n" + ex + "\r\n\r\n");
            }
            catch { }
            MessageBox.Show("Something went wrong:\r\n" + (ex == null ? "unknown error" : ex.Message) + "\r\n\r\nDetails were saved to " + Path.Combine(Settings.Dir, "errorlog.txt"),
                MainForm.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
