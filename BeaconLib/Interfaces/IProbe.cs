using System;
using System.Collections.Generic;
using BeaconLib.DTO;

namespace BeaconLib.Interfaces
{
    public interface IProbe
    {
        /// <summary>Start broadcasting Probe requests</summary>
        void Start();

        /// <summary>Stop broadcasting Probe requests</summary>
        void Stop();

        event Action<IEnumerable<BeaconLocation>> BeaconsUpdated;
    }
}