using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace Termors.Nuget.MDNSServiceDirectory
{
    public class ServiceEntry
    {
        public string Host { get; set; }
        public string Service { get; set; }
        public ushort Port { get; set; }

        private List<IPAddress> _ipAddresses = new List<IPAddress>();
        public IList<IPAddress> IPAddresses
        {
            get { return _ipAddresses; }
        }

        public override bool Equals(object obj)
        {
            ServiceEntry entry = obj as ServiceEntry;
            if (entry == null) return false;

            return Host.Equals(entry.Host) && Service.Equals(entry.Service) && (Port == entry.Port);
        }

        public override int GetHashCode()
        {
            return ToShortString().ToLowerInvariant().GetHashCode();
        }

        public virtual string ToShortString()
        {
            return String.Format("[{0}]//{1}:{2}", Service, Host, Port);
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder(ToShortString());

            sb.Append(" [ ");
            foreach (var ip in IPAddresses) sb.Append(ip.ToString()).Append(" ");
            sb.Append("]");

            return sb.ToString();
        }
    }

}
