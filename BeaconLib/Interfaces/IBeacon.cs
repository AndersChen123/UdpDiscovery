namespace BeaconLib.Interfaces
{
    public interface IBeacon
    {
        string BeaconType { get; }
        string BeaconData { get; set; }

        /// <summary>Start Listening for Probes</summary>
        void Start();

        /// <summary>Stop Listening for Probes</summary>
        void Stop();
    }
}