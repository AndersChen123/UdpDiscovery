using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using BeaconLib.DTO;
using BeaconLib.RemoteMachine;
using NLog;

namespace BeaconLib.Helpers
{
    public static class Utils
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        //for each network interface
        public static Dictionary<IPAddress, IPAddress> GetLocalIpAddress()
        {
            var localIp = new Dictionary<IPAddress, IPAddress>();
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if ((ni.OperationalStatus == OperationalStatus.Up && ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) ||
                    ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
                {
                    foreach (var ip in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            localIp.Add(ip.Address, ip.IPv4Mask);
                        }
                    }
                }
            }

            return localIp;
        }

        public static void BroadCastOnAllInterfaces(ref Dictionary<IPAddress, IPAddress> interfaces, ref byte[] data, BroadcastWay broadcastWay)
        {
            try
            {
                IPEndPoint broadcastEndpoint = null;

                Log.Trace($"BroadCastOnAllInterfaces with way: {broadcastWay}");

                //port is important, dont set it to 0,
                //must match between exposer and consumer
                if (broadcastWay == BroadcastWay.Client)
                {
                    broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, RemoteBeacon.DiscoveryPort);
                    Log.Trace($"Will broadcast on port: {RemoteBeacon.DiscoveryPort}");
                }
                else
                {
                    broadcastEndpoint = new IPEndPoint(IPAddress.Broadcast, RemoteProbe.DiscoveryPort);
                    Log.Trace($"Will broadcast on port: {RemoteProbe.DiscoveryPort}");
                }

                foreach (var ip in interfaces.Keys) // send the message to each network adapters
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        UdpClient clientInterface = null;
                        try
                        {
                            Log.Trace($"BroadcastProbe Invoked! To: {ip}");
                            clientInterface = new UdpClient(new IPEndPoint(ip, 0));
                            //if you dont dont enable broadcast effects will be only visible in local machine
                            clientInterface.EnableBroadcast = true;

                            //when setted to false discovery not working strangely
                            //by default its already true
                            //clientInterface.ExclusiveAddressUse = true;

                            clientInterface.Send(data, data.Length, broadcastEndpoint);
                        }
                        catch (Exception e)
                        {
                            Log.Debug("Unable to send\r\n" + e);
                        }
                        finally
                        {
                            clientInterface?.Close();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex);
            }
        }
    }
}