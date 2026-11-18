namespace Demo
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();

            // The workbench is a dark shell; the few surfaces WinForms draws itself (menus, combo
            // drop-downs, scrollbars) follow the OS colour mode unless the application opts in.
            Application.SetColorMode(SystemColorMode.Dark);

            Application.Run(new Form1());
        }
    }
}