using System.Drawing;
using System.Windows.Forms;
using Demo.Views.Workbench;
using Easel;
using Easel.Visuals;

namespace Demo
{
    internal static class Program
    {
        /// <summary>The main entry point for the application.</summary>
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();

            // Native islands (the focused text editor, the OS file dialogs) choose their colour mode when the
            // handle is created; the workbench itself is painted from the dark Easel palette.
            Application.SetColorMode(SystemColorMode.Dark);
            Theme.Mode = ThemeMode.Dark;

            var form = new Form
            {
                Text = "VeloxDev Workflow Demo",
                ClientSize = new Size(1440, 820),
                StartPosition = FormStartPosition.CenterScreen,
                WindowState = FormWindowState.Maximized,
            };

            // One HWND: the whole workbench, including the workflow surface, is one visual tree.
            var host = new VisualHost
            {
                Dock = DockStyle.Fill,
                BackColor = WorkbenchPalette.Window,
            };
            form.Controls.Add(host);

            var view = new WorkbenchView(host);
            host.Root = view;
            form.FormClosing += async (_, _) => await view.ShutdownAsync();
            view.Start();

            Application.Run(form);
        }
    }
}
