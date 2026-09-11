using CCRSwitch.Core;

namespace CCRSwitch;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // CLI 自检（与 mac 版一致），不启动 GUI
        if (args.Contains("--selftest")) { SelfTest.Run(live: false); return 0; }
        if (args.Contains("--selftest-live")) { SelfTest.Run(live: true); return 0; }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

        using var controller = new TrayController();
        controller.Run();
        return 0;
    }
}
