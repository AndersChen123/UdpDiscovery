using BeaconLib.Interfaces;
using BeaconLib.LocalMachine;
using BeaconLib.RemoteMachine;

namespace BeaconLib
{
    public class BeaconFactory
    {
        public IBeacon Get(bool isLocal, string chamber)
        {
            if (isLocal)
            {
                return new LocalBeacon(chamber);
            }

            return new RemoteBeacon(chamber);
        }
    }
}