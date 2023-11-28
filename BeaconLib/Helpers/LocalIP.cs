using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using NLog;

namespace BeaconLib.Helpers
{
    public static class LocalIp
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        //better way to improve performance for using correct algorythm to detect local Ip
        private static ushort CurGetIpMode { get; set; }

        /// <summary>Gives your ip on most public interface of all</summary>
        /// <returns></returns>
        public static string GetLocalIp()
        {
            var basicIp = "127.0.0.1";
            var localIp = basicIp;

            //select appropriate mode
            if (CurGetIpMode == 0)
            {
                if (IsNotWindowsOs())
                {
                    //other 2 ways anyway wont work on linux, at least for now
                    //just to avoid useless errors spam
                    //and differentiate OS management
                    Log.Debug("Linux detected, will perform only basic check");
                    localIp = BasicWayOfGettingLocalIp();
                    if (localIp.Equals(basicIp))
                    {
                        Log.Warn("Wasnt able to get public IP, will use default: " + basicIp);
                        CurGetIpMode = 4;
                    }
                    else
                    {
                        CurGetIpMode = 2;
                    }
                }
                else
                {
                    Log.Debug("Windows detected, more ways to obtain ip are available");
                    //we have windows so more tries can be done
                    localIp = GetMainLocalIpAddress();
                    if (localIp.Equals(basicIp))
                    {
                        //do second try
                        Log.Debug("Performing Second Check");

                        localIp = BasicWayOfGettingLocalIp();
                        if (localIp.Equals(basicIp))
                        {
                            Log.Debug("Performing Third Check");
                            localIp = GetMainLocalIpAddress(false);

                            if (!localIp.Equals(basicIp))
                            {
                                CurGetIpMode = 3;
                            }
                            else
                            {
                                //give default
                                CurGetIpMode = 4;
                            }
                        }
                        else
                        {
                            CurGetIpMode = 2;
                        }

                        if (localIp.Equals(basicIp))
                        {
                            Log.Debug("Wasnt able to find adequate Ip to use!!!");
                            Log.Debug("This is prerequisite!!!");
                            Log.Debug("Forcibly will use 127.0.0.1");
                        }
                    }
                    else
                    {
                        CurGetIpMode = 1;
                    }
                }

                Log.Debug("CurGetIpMode: " + CurGetIpMode);
            }
            else if (CurGetIpMode == 1)
            {
                localIp = GetMainLocalIpAddress(true, false);
            }
            else if (CurGetIpMode == 2)
            {
                localIp = BasicWayOfGettingLocalIp(false);
            }
            else if (CurGetIpMode == 3)
            {
                localIp = GetMainLocalIpAddress(false, false);
            }
            else if (CurGetIpMode == 4)
            {
                //leave at default
            }

            Log.Debug($"SELF IP: {localIp}");

            return localIp;
        }

        private static bool IsNotWindowsOs()
        {
            var res = true;
            var isWindows = Environment.Is64BitOperatingSystem && Environment.OSVersion.Platform == PlatformID.Win32NT;
            if (isWindows)
            {
                Log.Debug("This Peripheral Control is meant to be executed on Raspberry, with Linux OS");
                res = isWindows ? false : true;
            }
            else
            {
                Log.Debug("OS is linux, ok.");
            }

            return res;
        }

        public static string BasicWayOfGettingLocalIp(bool isCanLog = true)
        {
            var localIp = "127.0.0.1";

            try
            {
                using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
                {
                    socket.Connect("8.8.8.8", 65530);
                    var endPoint = socket.LocalEndPoint as IPEndPoint;
                    localIp = endPoint.Address.ToString();

                    socket?.Close();
                    socket?.Dispose();
                }

                CustomLog("Local ip: " + localIp, isCanLog);
            }
            catch (Exception ex)
            {
                Log.Debug(ex);
            }

            return localIp;
        }

        /// <summary>Seems to work only on windows for now, becouse you cant check property IsEligible</summary>
        /// <param name="considerJustThoseWithGateway"></param>
        /// <param name="isCanLog"></param>
        /// <returns></returns>
        public static string GetMainLocalIpAddress(bool considerJustThoseWithGateway = true, bool isCanLog = true)
        {
            try
            {
                UnicastIPAddressInformation mostSuitableIp = null;

                var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();

                foreach (var network in networkInterfaces)
                {
                    CustomLog("Analyzing Interface: " + network.Name, isCanLog);

                    if (network.OperationalStatus != OperationalStatus.Up)
                    {
                        CustomLog("Interface is probably down, will skip", isCanLog);
                        continue;
                    }

                    var properties = network.GetIPProperties();

                    if (considerJustThoseWithGateway)
                    {
                        if (properties.GatewayAddresses.Count == 0)
                        {
                            CustomLog("No gateway addresses found for this interface, will skip", isCanLog);
                            continue;
                        }
                    }

                    foreach (var address in properties.UnicastAddresses)
                    {
                        if (address.Address.AddressFamily != AddressFamily.InterNetwork)
                        {
                            CustomLog("Is Not InterNetwork", isCanLog);
                            continue;
                        }

                        if (IPAddress.IsLoopback(address.Address))
                        {
                            CustomLog("Is Not Loopback", isCanLog);
                            continue;
                        }

                        if (!address.IsDnsEligible)
                        {
                            CustomLog("Is not DNS eligible, will perform further check", isCanLog);
                            if (mostSuitableIp == null)
                            {
                                mostSuitableIp = address;
                            }

                            continue;
                        }

                        CustomLog("Is DNS eligible", isCanLog);

                        //Chose those who are NOT in DHCP
                        if (address.PrefixOrigin != PrefixOrigin.Dhcp)
                        {
                            CustomLog("Interface is Not in DHCP", isCanLog);
                            if (mostSuitableIp == null || !mostSuitableIp.IsDnsEligible)
                            {
                                mostSuitableIp = address;
                            }

                            continue;
                        }

                        CustomLog("Interface is in DHCP", isCanLog);

                        //seems if you got here, found address satisfies our requirements
                        var goodResult = address.Address.ToString();
                        CustomLog($"Found Ip: {goodResult}", isCanLog);

                        return goodResult;
                    }
                }

                var answer = mostSuitableIp != null
                    ? mostSuitableIp.Address.ToString()
                    : "127.0.0.1";

                CustomLog($"IP: {answer}", isCanLog);

                return answer;
            }
            catch (Exception ex)
            {
                Log.Debug(ex);
                return "127.0.0.1";
            }
        }

        private static void CustomLog(string message, bool isCanLog = true)
        {
            try
            {
                if (isCanLog)
                {
                    Log.Debug(message);
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex);
            }
        }
    }
}