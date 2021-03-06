namespace RedHatX.AspNetCore.Server.Kestrel.Transport.Linux
{
    public class LinuxTransportOptions
    {
        private int? _threadCount;
        private bool? _threadAffinity;

        public bool ReceiveOnIncomingCpu { get; set; } = false;

        public bool DeferAccept { get; set; } = true;

        public bool DeferSend { get; set; } = true;

        public CpuSet CpuSet { get; set; }

        public int ThreadCount
        {
            get => _threadCount ?? (CpuSet.IsEmpty ? Scheduler.GetAvailableCpusForProcess() : CpuSet.Cpus.Length);
            set => _threadCount = value;
        }

        public bool SetThreadAffinity
        {
            get => _threadAffinity ?? (ReceiveOnIncomingCpu || !CpuSet.IsEmpty);
            set => _threadAffinity = value;
        }
    }
}