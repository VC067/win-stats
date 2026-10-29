using System.ServiceProcess;

namespace WinStats.SensorService;

internal static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && args[0] == "--install")
            {
                string? driverPath = null;
                for (var i = 1; i < args.Length; i++)
                {
                    if (args[i] == "--driver" && i + 1 < args.Length)
                    {
                        driverPath = args[++i];
                        continue;
                    }

                    return 2;
                }

                return ServiceInstallation.Install(driverPath);
            }
            if (args is ["--remove"])
                return ServiceInstallation.Remove();
            if (args.Length != 0)
                return 2;

            ServiceBase.Run(new TemperatureService());
            return 0;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError("Win Stats sensor service: {0}", ex);
            return 1;
        }
    }
}
