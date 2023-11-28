using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using BeaconLib.Helpers;
using BeaconLib.Interfaces;
using NLog;

namespace BeaconLib.LocalMachine
{
    /// <summary>Instances of this class can be autodiscovered on the local network through UDP broadcasts</summary>
    /// <remarks>The advertisement consists of the beacon's application type and a short beacon-specific string.</remarks>
    public class LocalBeacon : IDisposable, IBeacon
    {
        internal const int DiscoveryPort = 35891;

        private static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private readonly UdpClient _udp;

        public LocalBeacon(string beaconType, ushort advertisedPort = 1234)
        {
            BeaconType = beaconType;
            AdvertisedPort = advertisedPort;
            BeaconData = "";

            _udp = new UdpClient();
            _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));

            try
            {
                _udp.AllowNatTraversal(true);
            }
            catch (Exception ex)
            {
                Log.Debug("Error switching on NAT traversal: ", ex.Message);
            }
        }

        public ushort AdvertisedPort { get; }
        public bool Stopped { get; private set; }

        public string BeaconType { get; }
        public string BeaconData { get; set; }

        public void Start()
        {
            Stopped = false;
            _udp.BeginReceive(ProbeReceived, null);
        }

        public void Stop()
        {
            Stopped = true;
        }

        public void Dispose()
        {
            Stop();
        }

        private void ProbeReceived(IAsyncResult ar)
        {
            Log.Trace("ProbeReceived Invoked!");

            var remote = new IPEndPoint(IPAddress.Any, 0);
            var bytes = _udp.EndReceive(ar, ref remote);

            // Compare beacon type to probe type
            var typeBytes = SharedMethods.Encode(BeaconType);
            if (SharedMethods.HasPrefix(bytes, typeBytes))
            {
                Log.Trace("Has prefix");
                // If true, respond again with our type, port and payload
                var responseData = SharedMethods.Encode(BeaconType)
                    .Concat(BitConverter.GetBytes((ushort)IPAddress.HostToNetworkOrder((short)AdvertisedPort)))
                    .Concat(SharedMethods.Encode(BeaconData)).ToArray();
                _udp.Send(responseData, responseData.Length, remote);
            }

            if (!Stopped)
            {
                _udp.BeginReceive(ProbeReceived, null);
            }
        }
    }
}