using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Termors.Nuget.MDNSServiceDirectory
{
    public class KeepaliveSocket : Socket
    {
        public KeepaliveSocket(IPAddress address, int port)
            : base(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
        {
            Address = address;
            Port = port;
        }

        public IPAddress Address
        {
            get;
        }

        public int Port
        {
            get;
        }

        public bool Disposed
        {
            get;
            protected set;
        } = false;

        public bool CanConnect(int timeout = 1000)
        {
            bool canConnect = false;

            try
            {
                var asyncResult = BeginConnect(Address, Port, (ar) => { }, null);

                asyncResult.AsyncWaitHandle.WaitOne(timeout);
                if (Connected) canConnect = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Socket exception in keepalive {0}, ip {1}, port {2}", ex.Message, Address, Port);
                canConnect = false;
            }

            return canConnect;

        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;

            base.Dispose(disposing);
        }

    }
}
