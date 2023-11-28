using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using BeaconLib.DTO;
using BeaconLib.Helpers;
using BeaconLib.Interfaces;
using NLog;

namespace BeaconLib.RemoteMachine
{
    /// <summary>Instances of this class can be autodiscovered on the local network through UDP broadcasts</summary>
    /// <remarks>The advertisement consists of the beacon's application type and a short beacon-specific string.</remarks>
    public class RemoteBeacon : IDisposable, IBeacon
    {
        internal const int DiscoveryPort = 35891;

        private static readonly Logger Log = LogManager.GetCurrentClassLogger();
        private readonly UdpClient _udp;

        private Dictionary<IPAddress, IPAddress> _localIpAddress; //key:IP address   value:subnetmask
        private IPEndPoint _sender = new IPEndPoint(0, 0);

        /// <summary>Advertised Port value is currently unused</summary>
        /// <param name="beaconType"></param>
        /// <param name="advertisedPort"></param>
        public RemoteBeacon(string beaconType, ushort advertisedPort = 1234)
        {
            _localIpAddress = Utils.GetLocalIpAddress();

            BeaconType = beaconType;
            //for now it seems not used
            AdvertisedPort = advertisedPort;
            BeaconData = "";

            _udp = new UdpClient();

            _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udp.Client.EnableBroadcast = true;
            _udp.Client.ExclusiveAddressUse = false;

            _udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));

            try
            {
                _udp.AllowNatTraversal(true);
            }
            catch (Exception ex)
            {
                Log.Debug("Error switching on NAT traversal: " + ex.Message);
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

            try
            {
                var bytes = _udp.EndReceive(ar, ref _sender);

                // Compare beacon type to probe type
                var typeBytes = SharedMethods.Encode(BeaconType);
                if (SharedMethods.HasPrefix(bytes, typeBytes))
                {
                    Log.Trace("Has prefix");
                    // If true, respond again with our type, port and payload
                    var responseData = SharedMethods.Encode(BeaconType)
                    .Concat(BitConverter.GetBytes((ushort)IPAddress.HostToNetworkOrder((short)AdvertisedPort)))
                    .Concat(SharedMethods.Encode(BeaconData)).ToArray();

                    Utils.BroadCastOnAllInterfaces(ref _localIpAddress, ref responseData, BroadcastWay.Server);
                }

                if (!Stopped)
                {
                    _udp.BeginReceive(ProbeReceived, null);
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex);
            }
        }
    }
}