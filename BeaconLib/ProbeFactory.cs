using BeaconLib.Interfaces;
using BeaconLib.LocalMachine;
using BeaconLib.RemoteMachine;

namespace BeaconLib
{
    public class ProbeFactory
    {
        public IProbe Get(bool isLocal, string chamber)
        {
            if (isLocal)
            {
                return new LocalProbe(chamber);
            }

            return new RemoteProbe(chamber);
        }
    }
}