namespace Tmds.Kestrel.Linux
{
    public enum ReadStrategy
    {
        // Attempt to read a fixed amount of data
        // This may leave data pending
        Fixed,
        // Read data based on how much is available
        // Requires querying amount of available data
        Available
    }

    public class TransportOptions
    {
        public int ThreadCount { get; set; } = AvailableProcessors;

        public bool SetThreadAffinity { get; set; } = NotConstrained;

        public bool ReceiveOnIncomingCpu { get; set; } = NotConstrained;

        public bool DeferAccept { get; set; } = true;

        public bool CoalesceWrites { get; set; } = true;

        public ReadStrategy ReadStrategy { get; set; } = ReadStrategy.Available;

        private static int AvailableProcessors => Scheduler.GetAvailableCpusForProcess();

        private static bool NotConstrained => Scheduler.GetAvailableCpusForProcess() == CpuInfo.GetAvailableCpus();
    }
}