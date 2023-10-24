using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Topshelf;

namespace IpMonitor {
    internal class WindowsServiceConfigure {
        private static string APP_NAME = "IP监听器";
        private static string ServiceName = "AppIpMonitor";

        internal static void Configure() {
            var rc = HostFactory.Run(host =>                                    // 1
            {
                host.Service<WinSwService>(service =>                   // 2
                {
                    service.ConstructUsing(() => new WinSwService());   // 3
                    service.WhenStarted(s => s.Start());                        // 4
                    service.WhenStopped(s => s.Stop());                         // 5
                });

                host.RunAsLocalSystem();                                        // 6

                host.EnableServiceRecovery(service =>                           // 7
                {
                    service.RestartService(3);                                  // 8
                });
                host.SetDescription(APP_NAME + "服务");       // 9
                host.SetDisplayName(ServiceName);                   // 10
                host.SetServiceName(ServiceName);                     // 11
                host.StartAutomaticallyDelayed();                               // 12
            });

            var exitCode = (int)Convert.ChangeType(rc, rc.GetTypeCode());       // 13
            Environment.ExitCode = exitCode;
        }
    }
}
