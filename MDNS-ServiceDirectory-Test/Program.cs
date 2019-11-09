using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Termors.Nuget.MDNSServiceDirectory;

namespace Termors.Development.Test.MDNSServiceDirectoryTest
{

    class MainClass
    {
        private static ServiceDirectory sd = new ServiceDirectory(filterfunc: FilterHippoServices);

        private static bool FilterHippoServices(string arg)
        {
            return arg.ToLowerInvariant().Contains("hippohttp");
        }

        public static void Main(string[] args)
        {
            Init();

            // Schedule first task
            ScheduleTask();

            // Run until Ctrl+C
            var endEvent = new ManualResetEvent(false);

            Console.CancelKeyPress += (sender, e) =>
            {
                endEvent.Set();
            };

            endEvent.WaitOne();
        }

        private static void Init()
        {
            sd.KeepaliveCheckInterval = 10;
            sd.KeepaliveTcp = true;

            sd.HostDiscovered += (dir, svc) => { Console.WriteLine("Service discovered: {0}", svc); };
            sd.HostRemoved += (dir, svc) => { Console.WriteLine("Service removed: {0}", svc); };
            sd.HostUpdated += (dir, svc) => { Console.WriteLine("Service updated: {0}", svc); };

            sd.Init();
        }

        private static void ScheduleTask()
        {
            Task.Run(() => EnumerateServices_Mdns());
        }

        private static void EnumerateServices_Mdns()
        {
            Thread.Sleep(30000);

            Console.WriteLine("\n\n========== HOSTS at {0} =========", DateTime.Now);

            lock (sd.Services)
                foreach (var service in sd.Services)
                {
                    Console.WriteLine(service.ToString());
                }
            Console.WriteLine("\n\n========== END ===========");

            ScheduleTask();
        }

    }
}
