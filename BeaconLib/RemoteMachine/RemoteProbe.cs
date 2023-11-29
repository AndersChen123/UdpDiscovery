using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Timers;
using BeaconLib.DTO;
using BeaconLib.Helpers;
using BeaconLib.Interfaces;
using NLog;
using Timer = System.Timers.Timer;

namespace BeaconLib.RemoteMachine
{
    /// <summary>Counterpart of the beacon, searches for beacons</summary>
    /// <remarks>The beacon list event will not be raised on your main thread!</remarks>
    public class RemoteProbe : IDisposable, IProbe
    {
        internal const int DiscoveryPort = 35890;

        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        /// <summary>Remove beacons older than this</summary>
        private static readonly TimeSpan BeaconTimeout = new TimeSpan(0, 0, 0, 10); // seconds

        private static readonly object Locker = new object();

        private bool _running = true;

        private Dictionary<IPAddress, IPAddress> _localIpAddress; //key:IP address   value:subnetmask
        private IEnumerable<BeaconLocation> _currentBeacons = Enumerable.Empty<BeaconLocation>();
        private IPEndPoint _sender = new IPEndPoint(0, 0);

        private UdpClient _udp = new UdpClient();

        public RemoteProbe(string beaconType)
        {
            BeaconType = beaconType;
            ProbeTimer = new Timer(2000);
            ProbeTimer.Elapsed += BackgroundLoop;
            ProbeTimer.AutoReset = true;
        }

        private Timer ProbeTimer
        {
            get;
        }

        public string BeaconType
        {
            get;
        }

        public void Dispose()
        {
            try
            {
                Stop();
            }
            catch (Exception ex)
            {
                Log.Debug(ex);
            }
        }

        public event Action<IEnumerable<BeaconLocation>> BeaconsUpdated;

        public void Start()
        {
            _running = true;
            Init();
            ProbeTimer?.Start();
            //start asap first time without waiting timer reset
            BackgroundLoop(null, null);
        }

        public void Stop()
        {
            try
            {
                _running = false;
                ProbeTimer?.Stop();
                _udp?.Close();
                _udp?.Dispose();
            }
            catch (Exception)
            {
                // Ignored
            }
        }

        private void Init()
        {
            _localIpAddress = Utils.GetLocalIpAddress();

            _udp = new UdpClient();
            _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            //udp.Client.EnableBroadcast = true;
            //udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            //udp.Client.ExclusiveAddressUse = false;

            _udp.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
            try
            {
                Log.Trace("Enabling NAT Traversal");
                _udp.AllowNatTraversal(true);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Error switching on NAT traversal: ");
            }

            _udp.BeginReceive(ResponseReceived, null);
        }

        private void ResponseReceived(IAsyncResult ar)
        {
            Log.Trace("ResponseReceived Invoked!");

            try
            {
                var bytes = _udp.EndReceive(ar, ref _sender);

                var typeBytes = SharedMethods.Encode(BeaconType).ToList();
                Log.Trace(string.Join(", ", typeBytes.Select(_ => (char)_)));
                if (SharedMethods.HasPrefix(bytes, typeBytes))
                {
                    Log.Trace("Beacon has prefix");
                    try
                    {
                        var portBytes = bytes.Skip(typeBytes.Count()).Take(2).ToArray();
                        var port = (ushort)IPAddress.NetworkToHostOrder((short)BitConverter.ToUInt16(portBytes, 0));
                        var payload = SharedMethods.Decode(bytes.Skip(typeBytes.Count() + 2));

                        Log.Trace($"Port: {port}, Payload: {payload}");
                        NewBeacon(new BeaconLocation(new IPEndPoint(_sender.Address, port), payload, DateTime.Now));
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex);
                    }
                }

                if (_running)
                {
                    _udp.BeginReceive(ResponseReceived, null);
                }
            }
            catch (Exception)
            {
                // Ignored
            }
        }

        private void BackgroundLoop(object sender, ElapsedEventArgs e)
        {
            var hasLock = false;

            try
            {
                Monitor.TryEnter(Locker, ref hasLock);
                if (!hasLock)
                {
                    Log.Trace("Concurrent Thread isnt finished, wait next time");
                    return;
                }

                Log.Trace("Lock Acquired!");

                PruneBeacons();
                BroadcastProbe();
            }
            catch (Exception ex)
            {
                Log.Debug(ex);
            }
            finally
            {
                if (hasLock)
                {
                    Monitor.Exit(Locker);
                    Log.Trace("Lock Released");
                }
            }
        }

        private void BroadcastProbe()
        {
            var probe = SharedMethods.Encode(BeaconType).ToArray();
            Utils.BroadCastOnAllInterfaces(ref _localIpAddress, ref probe, BroadcastWay.Client);
        }

        private void PruneBeacons()
        {
            var cutOff = DateTime.Now - BeaconTimeout;
            var oldBeacons = _currentBeacons.ToList();
            var newBeacons = oldBeacons.Where(_ => _.LastAdvertised >= cutOff).ToList();
            if (EnumsEqual(oldBeacons, newBeacons))
            {
                Log.Trace("new beacon is same as old, nothing to do");
                return;
            }

            var u = BeaconsUpdated;
            if (u != null)
            {
                u(newBeacons);
            }

            _currentBeacons = newBeacons;
        }

        private void NewBeacon(BeaconLocation newBeacon)
        {
            var newBeacons = _currentBeacons
                .Where(_ => !_.Equals(newBeacon))
                .Concat(new[] { newBeacon })
                .OrderBy(_ => _.Data)
                .ThenBy(_ => _.Address, IpEndPointComparer.Instance)
                .ToList();
            var u = BeaconsUpdated;
            if (u != null)
            {
                u(newBeacons);
            }

            _currentBeacons = newBeacons;
        }

        private static bool EnumsEqual<T>(IEnumerable<T> xs, IEnumerable<T> ys)
        {
            return xs.Zip(ys, (x, y) => x.Equals(y)).Count() == xs.Count();
        }
    }
}