using System;
using System.Collections.Generic;
using System.Diagnostics;
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

        private static bool LogDiscovery { get; set; } = false;
        private static bool LogUpdate { get; set; } = false;
        private static bool LogRemoval { get; set; } = false;

        public static void Main(string[] args)
        {
            ParseArgs(args);

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

        private static void ParseArgs(string[] args)
        {
            List<string> argsLower = new List<string>(args.Length);
            foreach (var s in args) argsLower.Add(s.ToLowerInvariant());

            if (argsLower.Contains("-d") || argsLower.Contains("--discovery")) LogDiscovery = true;
            if (argsLower.Contains("-u") || argsLower.Contains("--update")) LogUpdate = true;
            if (argsLower.Contains("-r") || argsLower.Contains("--removal")) LogRemoval = true;
        }

        private static void Init()
        {
            Debug.Listeners.Add(new ConsoleTraceListener());

            sd.KeepaliveCheckInterval = 10;
            sd.KeepaliveTcp = true;

            sd.HostDiscovered += (dir, svc) => { if (LogDiscovery) Console.WriteLine("Service discovered: {0}", svc); };
            sd.HostRemoved += (dir, svc) => { if (LogRemoval) Console.WriteLine("Service removed: {0}", svc); };
            sd.HostUpdated += (dir, svc) => { if (LogUpdate) Console.WriteLine("Service updated: {0}", svc); };

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
