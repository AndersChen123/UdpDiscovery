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

namespace BeaconLib.LocalMachine
{
    /// <summary>Counterpart of the beacon, searches for beacons</summary>
    /// <remarks>The beacon list event will not be raised on your main thread!</remarks>
    public class LocalProbe : IDisposable, IProbe
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        /// <summary>Remove beacons older than this</summary>
        private static readonly TimeSpan BeaconTimeout = new TimeSpan(0, 0, 0, 5); // seconds

        private static readonly object Locker = new object();

        private readonly UdpClient _udp = new UdpClient();

        private bool _running = true;
        private IEnumerable<BeaconLocation> _currentBeacons = Enumerable.Empty<BeaconLocation>();

        public LocalProbe(string beaconType)
        {
            BeaconType = beaconType;
            ProbeTimer = new Timer(2000);
            ProbeTimer.Elapsed += BackgroundLoop;
            ProbeTimer.AutoReset = true;
        }

        private Timer ProbeTimer { get; }

        public string BeaconType { get; }

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
            _running = false;
            ProbeTimer?.Stop();
            _udp?.Close();
            _udp?.Dispose();
        }

        private void Init()
        {
            _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            _udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
            try
            {
                _udp.AllowNatTraversal(true);
            }
            catch (Exception ex)
            {
                Log.Debug("Error switching on NAT traversal: " + ex.Message);
            }

            _udp.BeginReceive(ResponseReceived, null);
        }

        private void ResponseReceived(IAsyncResult ar)
        {
            Log.Trace("ResponseReceived Invoked!");

            var remote = new IPEndPoint(IPAddress.Any, 0);
            var bytes = _udp.EndReceive(ar, ref remote);

            var typeBytes = SharedMethods.Encode(BeaconType).ToList();
            Log.Debug(string.Join(", ", typeBytes.Select(_ => (char)_)));
            if (SharedMethods.HasPrefix(bytes, typeBytes))
            {
                try
                {
                    Log.Trace("Beacon has prefix");
                    var portBytes = bytes.Skip(typeBytes.Count()).Take(2).ToArray();
                    var port = (ushort)IPAddress.NetworkToHostOrder((short)BitConverter.ToUInt16(portBytes, 0));
                    var payload = SharedMethods.Decode(bytes.Skip(typeBytes.Count() + 2));
                    NewBeacon(new BeaconLocation(new IPEndPoint(remote.Address, port), payload, DateTime.Now));
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
            _udp.Send(probe, probe.Length, new IPEndPoint(IPAddress.Broadcast, LocalBeacon.DiscoveryPort));
        }

        private void PruneBeacons()
        {
            var cutOff = DateTime.Now - BeaconTimeout;
            var oldBeacons = _currentBeacons.ToList();
            var newBeacons = oldBeacons.Where(_ => _.LastAdvertised >= cutOff).ToList();
            if (EnumsEqual(oldBeacons, newBeacons))
            {
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