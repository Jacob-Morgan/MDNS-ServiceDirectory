using System;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


using Makaretu.Dns;

namespace Termors.Nuget.MDNSServiceDirectory
{
    public delegate void HostEvent(ServiceDirectory directory, ServiceEntry entry);

    /// <summary>
    /// Directory of MDNS hosts that automatically keeps itself updated as services appear and disappear
    /// </summary>
    public class ServiceDirectory : IDisposable
    {
        protected List<ServiceEntry> _entries = new List<ServiceEntry>();
        protected MulticastService _mdns = new MulticastService();
        protected readonly ServiceDiscovery _sd;
        protected ManualResetEvent _stopKeepAlive = new ManualResetEvent(false);
        private bool _disposedValue = false; // To detect redundant calls


        public ServiceDirectory(Func<string,bool> filterfunc = null)
        {
            FilterFunction = filterfunc;

            _sd = new ServiceDiscovery(_mdns);
        }

        public uint KeepaliveCheckInterval
        {
            get; set;
        } = 60;

        public bool KeepalivePing
        {
            get; set;
        } = true;

        public bool KeepaliveTcp
        {
            get; set;
        } = false;

        public Func<string,bool> FilterFunction
        {
            get; set;
        }

        public IList<ServiceEntry> Services
        {
            get { return _entries; }
        }

        public event HostEvent HostDiscovered;
        public event HostEvent HostRemoved;
        public event HostEvent HostUpdated;

        protected virtual void AddService(ServiceEntry entry)
        {
            bool bNew = false;

            if (_disposedValue) throw new ObjectDisposedException("ServiceDirectory");

            lock (_entries)
            {
                if (!_entries.Contains(entry))
                {
                    _entries.Add(entry);
                    bNew = true;

                    Debug.WriteLine("Added new service: {0}", entry);
                }
            }

            // Fire event if a new host was indeed added
            if (bNew && HostDiscovered != null) HostDiscovered(this, entry);
        }

        protected virtual void RemoveService(ServiceEntry entry)
        {
            bool bContains = false;

            if (_disposedValue) throw new ObjectDisposedException("ServiceDirectory");

            lock (_entries)
            {
                bContains = _entries.Contains(entry);
                if (bContains) _entries.Remove(entry);
            }

            // Fire event if host was indeed removed
            if (bContains && HostRemoved != null) HostRemoved(this, entry);
        }

        protected virtual void RemoveServiceByName(string name)
        {
            //TODO
        }

        public virtual void Init()
        {
            if (_disposedValue) throw new ObjectDisposedException("ServiceDirectory");

            _mdns.NetworkInterfaceDiscovered += (s, e) =>
            {
                // Ask for the name of all services.
                _sd.QueryAllServices();
            };

            _sd.ServiceDiscovered += (s, serviceName) =>
            {
                // Check if this is a service we're interested in
                bool interesting = true;
                if (FilterFunction != null)
                {
                    interesting = FilterFunction(serviceName.ToString());
                }
                if (!interesting) return;

                // Ask for the name of instances of the service.
                _mdns.SendQuery(serviceName, type: DnsType.PTR);
            };

            _sd.ServiceInstanceDiscovered += (s, e) =>
            {
                // Ask for the service instance details.
                _mdns.SendQuery(e.ServiceInstanceName, type: DnsType.SRV);
            };

            _sd.ServiceInstanceShutdown += (s, e) =>
            {
                Debug.WriteLine("Removing service instance {0} from MDNS message", e.ServiceInstanceName);
                RemoveServiceByName(e.ServiceInstanceName.ToString());
            };

            _mdns.AnswerReceived += (s, e) =>
            {
                // Is this an answer to a service instance details?
                var servers = e.Message.Answers.OfType<SRVRecord>();
                foreach (var server in servers)
                {
                    // For some reason, some services slip through the cracks of the first filter at ServiceDiscovered
                    // and we have to filter again.
                    bool interesting = true;
                    if (FilterFunction != null)
                    {
                        interesting = FilterFunction(server.Name.ToString());
                    }
                    if (!interesting) continue;


                    var newSvc = new ServiceEntry
                    {
                        Host = server.Target.ToString(),
                        Port = server.Port,
                        Service = server.Name.ToString()
                    };

                    AddService(newSvc);

                    // Query IP addresses (only IPv4)
                    _mdns.SendQuery(server.Target, type: DnsType.A);
                }

                // Is this an answer to host addresses?
                var addresses = e.Message.Answers.OfType<AddressRecord>();
                foreach (var address in addresses)
                {
                    // Find corresponding host
                    lock (_entries)
                    {
                        var host = from entry in _entries where entry.Host == address.Name select entry;
                        foreach (var h in host) 
                        {
                            if (! h.IPAddresses.Contains(address.Address)) h.IPAddresses.Add(address.Address);
                            HostUpdated?.Invoke(this, h);
                        }
                    }
                }
            };

            _mdns.Start();

            ScheduleKeepAliveCheck();
        }

        private void ScheduleKeepAliveCheck()
        {
            Task.Run(() => CheckAlive());
        }

        private void CheckAlive()
        {
            if (_disposedValue) return;     // No exception, could be scheduling race condition with Dispose() call

            bool shouldWeStop = _stopKeepAlive.WaitOne(((int) KeepaliveCheckInterval) * 1000);
            if (shouldWeStop) return;

            // Send each of the services a Ping
            ServiceEntry[] copyOfServices;
            lock (_entries)
            {
                copyOfServices = new ServiceEntry[_entries.Count];
                _entries.CopyTo(copyOfServices);
            }

            foreach (var svc in copyOfServices)
            {
                foreach (var ip in svc.IPAddresses)
                {
                    Task.Run(() => CheckKeepalive(ip, svc) );
                }
            }

            // Check for new services.
            _sd.QueryAllServices();

            ScheduleKeepAliveCheck();
        }

        protected virtual void CheckKeepalive(IPAddress ip, ServiceEntry svc)
        {
            if (KeepalivePing)
            {
                Ping ping = new Ping();
                var result = ping.Send(ip, 1000);

                if (result.Status != IPStatus.Success)
                {
                    Debug.WriteLine("Removed service {0} at {1} due to keepalive ping failure", svc.Service, svc.Host);
                    RemoveService(svc);
                    return;             
                }
            }

            if (KeepaliveTcp)
            {
                using (var sock = new KeepaliveSocket(ip, svc.Port))
                {
                    bool tcpConnected = sock.CanConnect();
                    if (!tcpConnected)
                    {
                        Debug.WriteLine("Removed service {0} at {1}:{2} due to Tcp Connect failure", svc.Service, svc.Host, svc.Port);
                        RemoveService(svc);
                        return;
                    }
                }
            }

            // Service is alive
            svc.LastSeenAlive = DateTime.Now;
            HostUpdated?.Invoke(this, svc);
        }

        #region IDisposable Support

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    _mdns.Dispose();
                    _sd.Dispose();
                }

                _disposedValue = true;
            }
        }


        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
        }
        #endregion
    }
}
