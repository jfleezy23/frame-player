using System;
using System.Runtime.Serialization;

namespace FramePlayer.Diagnostics
{
    [DataContract]
    internal sealed class DualPaneBudgetHarnessPairRequest
    {
        public DualPaneBudgetHarnessPairRequest(string label, string primaryPath, string comparePath, bool requirePaneAlignment)
        {
            Label = label ?? string.Empty;
            PrimaryPath = primaryPath ?? string.Empty;
            ComparePath = comparePath ?? string.Empty;
            RequirePaneAlignment = requirePaneAlignment;
        }

        [DataMember(Name = "label")]
        public string Label { get; private set; }

        [DataMember(Name = "primaryPath")]
        public string PrimaryPath { get; private set; }

        [DataMember(Name = "comparePath")]
        public string ComparePath { get; private set; }

        [DataMember(Name = "requirePaneAlignment")]
        public bool RequirePaneAlignment { get; private set; }
    }

    [DataContract]
    internal sealed class DualPaneBudgetHarnessHostScenarioRequest
    {
        public DualPaneBudgetHarnessHostScenarioRequest(string name, long totalPhysicalMemoryBytes, long availablePhysicalMemoryBytes)
        {
            Name = name ?? string.Empty;
            TotalPhysicalMemoryBytes = totalPhysicalMemoryBytes;
            AvailablePhysicalMemoryBytes = availablePhysicalMemoryBytes;
        }

        [DataMember(Name = "name")]
        public string Name { get; private set; }

        [DataMember(Name = "totalPhysicalMemoryBytes")]
        public long TotalPhysicalMemoryBytes { get; private set; }

        [DataMember(Name = "availablePhysicalMemoryBytes")]
        public long AvailablePhysicalMemoryBytes { get; private set; }
    }

    [DataContract]
    internal sealed class DualPaneBudgetHarnessRequest
    {
        [DataMember(Name = "pairs")]
        public DualPaneBudgetHarnessPairRequest[] Pairs { get; set; }

        [DataMember(Name = "hostScenarios")]
        public DualPaneBudgetHarnessHostScenarioRequest[] HostScenarios { get; set; }

        [DataMember(Name = "reportJsonPath")]
        public string ReportJsonPath { get; set; }

        [DataMember(Name = "errorPath")]
        public string ErrorPath { get; set; }
    }

    [DataContract]
    public sealed class DualPaneBudgetHarnessReport
    {
        public DualPaneBudgetHarnessReport(
            string generatedAtUtc,
            DualPaneBudgetHarnessHostScenarioReport[] hostScenarios,
            DualPaneBudgetHarnessSummary summary)
        {
            GeneratedAtUtc = generatedAtUtc ?? string.Empty;
            HostScenarios = hostScenarios ?? Array.Empty<DualPaneBudgetHarnessHostScenarioReport>();
            Summary = summary ?? new DualPaneBudgetHarnessSummary(0, 0, 0, 0, 0);
        }

        [DataMember(Name = "generatedAtUtc")]
        public string GeneratedAtUtc { get; private set; }

        [DataMember(Name = "hostScenarios")]
        public DualPaneBudgetHarnessHostScenarioReport[] HostScenarios { get; private set; }

        [DataMember(Name = "summary")]
        public DualPaneBudgetHarnessSummary Summary { get; private set; }
    }

    [DataContract]
    public sealed class DualPaneBudgetHarnessHostScenarioReport
    {
        public DualPaneBudgetHarnessHostScenarioReport(
            string name,
            long totalPhysicalMemoryBytes,
            long availablePhysicalMemoryBytes,
            DualPaneBudgetHarnessPairReport[] pairReports,
            DualPaneBudgetHarnessHostSummary summary)
        {
            Name = name ?? string.Empty;
            TotalPhysicalMemoryBytes = totalPhysicalMemoryBytes;
            AvailablePhysicalMemoryBytes = availablePhysicalMemoryBytes;
            PairReports = pairReports ?? Array.Empty<DualPaneBudgetHarnessPairReport>();
            Summary = summary ?? new DualPaneBudgetHarnessHostSummary(0, 0, 0, 0, 0);
        }

        [DataMember(Name = "name")]
        public string Name { get; private set; }

        [DataMember(Name = "totalPhysicalMemoryBytes")]
        public long TotalPhysicalMemoryBytes { get; private set; }

        [DataMember(Name = "availablePhysicalMemoryBytes")]
        public long AvailablePhysicalMemoryBytes { get; private set; }

        [DataMember(Name = "pairReports")]
        public DualPaneBudgetHarnessPairReport[] PairReports { get; private set; }

        [DataMember(Name = "summary")]
        public DualPaneBudgetHarnessHostSummary Summary { get; private set; }
    }

    [DataContract]
    public sealed class DualPaneBudgetHarnessPairReport
    {
        public DualPaneBudgetHarnessPairReport(
            string label,
            string primaryPath,
            string comparePath,
            bool requirePaneAlignment,
            string hostScenarioName,
            long totalPhysicalMemoryBytes,
            long availablePhysicalMemoryBytes,
            double primaryOpenMilliseconds,
            double compareOpenMilliseconds,
            double primaryIndexReadyMilliseconds,
            double compareIndexReadyMilliseconds,
            double seekToTimeMilliseconds,
            int stepWindow,
            int allPanePaneCount,
            DualPaneBudgetHarnessStepMetrics primaryStepMetrics,
            DualPaneBudgetHarnessStepMetrics compareStepMetrics,
            DualPaneBudgetHarnessDecodeSnapshot primaryDecode,
            DualPaneBudgetHarnessDecodeSnapshot compareDecode,
            DualPaneBudgetHarnessCheck[] checks,
            DualPaneBudgetHarnessPairSummary summary,
            string harnessError)
        {
            Label = label ?? string.Empty;
            PrimaryPath = primaryPath ?? string.Empty;
            ComparePath = comparePath ?? string.Empty;
            RequirePaneAlignment = requirePaneAlignment;
            HostScenarioName = hostScenarioName ?? string.Empty;
            TotalPhysicalMemoryBytes = totalPhysicalMemoryBytes;
            AvailablePhysicalMemoryBytes = availablePhysicalMemoryBytes;
            PrimaryOpenMilliseconds = primaryOpenMilliseconds;
            CompareOpenMilliseconds = compareOpenMilliseconds;
            PrimaryIndexReadyMilliseconds = primaryIndexReadyMilliseconds;
            CompareIndexReadyMilliseconds = compareIndexReadyMilliseconds;
            SeekToTimeMilliseconds = seekToTimeMilliseconds;
            StepWindow = stepWindow;
            AllPanePaneCount = allPanePaneCount;
            PrimaryStepMetrics = primaryStepMetrics ?? new DualPaneBudgetHarnessStepMetrics();
            CompareStepMetrics = compareStepMetrics ?? new DualPaneBudgetHarnessStepMetrics();
            PrimaryDecode = primaryDecode ?? DualPaneBudgetHarnessDecodeSnapshot.Empty;
            CompareDecode = compareDecode ?? DualPaneBudgetHarnessDecodeSnapshot.Empty;
            Checks = checks ?? Array.Empty<DualPaneBudgetHarnessCheck>();
            Summary = summary ?? new DualPaneBudgetHarnessPairSummary(0, 0, 0);
            HarnessError = harnessError ?? string.Empty;
        }

        [DataMember(Name = "label")]
        public string Label { get; private set; }

        [DataMember(Name = "primaryPath")]
        public string PrimaryPath { get; private set; }

        [DataMember(Name = "comparePath")]
        public string ComparePath { get; private set; }

        [DataMember(Name = "requirePaneAlignment")]
        public bool RequirePaneAlignment { get; private set; }

        [DataMember(Name = "hostScenarioName")]
        public string HostScenarioName { get; private set; }

        [DataMember(Name = "totalPhysicalMemoryBytes")]
        public long TotalPhysicalMemoryBytes { get; private set; }

        [DataMember(Name = "availablePhysicalMemoryBytes")]
        public long AvailablePhysicalMemoryBytes { get; private set; }

        [DataMember(Name = "primaryOpenMilliseconds")]
        public double PrimaryOpenMilliseconds { get; private set; }

        [DataMember(Name = "compareOpenMilliseconds")]
        public double CompareOpenMilliseconds { get; private set; }

        [DataMember(Name = "primaryIndexReadyMilliseconds")]
        public double PrimaryIndexReadyMilliseconds { get; private set; }

        [DataMember(Name = "compareIndexReadyMilliseconds")]
        public double CompareIndexReadyMilliseconds { get; private set; }

        [DataMember(Name = "seekToTimeMilliseconds")]
        public double SeekToTimeMilliseconds { get; private set; }

        [DataMember(Name = "stepWindow")]
        public int StepWindow { get; private set; }

        [DataMember(Name = "allPanePaneCount")]
        public int AllPanePaneCount { get; private set; }

        [DataMember(Name = "primaryStepMetrics")]
        public DualPaneBudgetHarnessStepMetrics PrimaryStepMetrics { get; private set; }

        [DataMember(Name = "compareStepMetrics")]
        public DualPaneBudgetHarnessStepMetrics CompareStepMetrics { get; private set; }

        [DataMember(Name = "primaryDecode")]
        public DualPaneBudgetHarnessDecodeSnapshot PrimaryDecode { get; private set; }

        [DataMember(Name = "compareDecode")]
        public DualPaneBudgetHarnessDecodeSnapshot CompareDecode { get; private set; }

        [DataMember(Name = "checks")]
        public DualPaneBudgetHarnessCheck[] Checks { get; private set; }

        [DataMember(Name = "summary")]
        public DualPaneBudgetHarnessPairSummary Summary { get; private set; }

        [DataMember(Name = "harnessError")]
        public string HarnessError { get; private set; }
    }

    [DataContract]
    public sealed class DualPaneBudgetHarnessDecodeSnapshot
    {
        public static DualPaneBudgetHarnessDecodeSnapshot Empty { get; } = new DualPaneBudgetHarnessDecodeSnapshot(
            string.Empty,
            string.Empty,
            false,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            0L,
            0L,
            0,
            0,
            0,
            0,
            0,
            0d,
            0d,
            0L,
            false,
            string.Empty,
            string.Empty,
            0d);

        public DualPaneBudgetHarnessDecodeSnapshot(
            string activeDecodeBackend,
            string actualBackendUsed,
            bool isGpuActive,
            string gpuCapabilityStatus,
            string gpuFallbackReason,
            string budgetBand,
            string hostResourceClass,
            long sessionBudgetBytes,
            long paneBudgetBytes,
            int maxPreviousCachedFrames,
            int maxForwardCachedFrames,
            int observedPreviousCachedFrames,
            int observedForwardCachedFrames,
            int operationalQueueDepth,
            double hardwareFrameTransferMilliseconds,
            double bgraConversionMilliseconds,
            long indexedFrameCount,
            bool isGlobalFrameIndexAvailable,
            string globalFrameIndexStatus,
            string videoCodecName,
            double framesPerSecond)
        {
            ActiveDecodeBackend = activeDecodeBackend ?? string.Empty;
            ActualBackendUsed = actualBackendUsed ?? string.Empty;
            IsGpuActive = isGpuActive;
            GpuCapabilityStatus = gpuCapabilityStatus ?? string.Empty;
            GpuFallbackReason = gpuFallbackReason ?? string.Empty;
            BudgetBand = budgetBand ?? string.Empty;
            HostResourceClass = hostResourceClass ?? string.Empty;
            SessionBudgetBytes = sessionBudgetBytes;
            PaneBudgetBytes = paneBudgetBytes;
            MaxPreviousCachedFrames = maxPreviousCachedFrames;
            MaxForwardCachedFrames = maxForwardCachedFrames;
            ObservedPreviousCachedFrames = observedPreviousCachedFrames;
            ObservedForwardCachedFrames = observedForwardCachedFrames;
            OperationalQueueDepth = operationalQueueDepth;
            HardwareFrameTransferMilliseconds = hardwareFrameTransferMilliseconds;
            BgraConversionMilliseconds = bgraConversionMilliseconds;
            IndexedFrameCount = indexedFrameCount;
            IsGlobalFrameIndexAvailable = isGlobalFrameIndexAvailable;
            GlobalFrameIndexStatus = globalFrameIndexStatus ?? string.Empty;
            VideoCodecName = videoCodecName ?? string.Empty;
            FramesPerSecond = framesPerSecond;
        }

        [DataMember(Name = "activeDecodeBackend")]
        public string ActiveDecodeBackend { get; private set; }

        [DataMember(Name = "actualBackendUsed")]
        public string ActualBackendUsed { get; private set; }

        [DataMember(Name = "isGpuActive")]
        public bool IsGpuActive { get; private set; }

        [DataMember(Name = "gpuCapabilityStatus")]
        public string GpuCapabilityStatus { get; private set; }

        [DataMember(Name = "gpuFallbackReason")]
        public string GpuFallbackReason { get; private set; }

        [DataMember(Name = "budgetBand")]
        public string BudgetBand { get; private set; }

        [DataMember(Name = "hostResourceClass")]
        public string HostResourceClass { get; private set; }

        [DataMember(Name = "sessionBudgetBytes")]
        public long SessionBudgetBytes { get; private set; }

        [DataMember(Name = "paneBudgetBytes")]
        public long PaneBudgetBytes { get; private set; }

        [DataMember(Name = "maxPreviousCachedFrames")]
        public int MaxPreviousCachedFrames { get; private set; }

        [DataMember(Name = "maxForwardCachedFrames")]
        public int MaxForwardCachedFrames { get; private set; }

        [DataMember(Name = "observedPreviousCachedFrames")]
        public int ObservedPreviousCachedFrames { get; private set; }

        [DataMember(Name = "observedForwardCachedFrames")]
        public int ObservedForwardCachedFrames { get; private set; }

        [DataMember(Name = "operationalQueueDepth")]
        public int OperationalQueueDepth { get; private set; }

        [DataMember(Name = "hardwareFrameTransferMilliseconds")]
        public double HardwareFrameTransferMilliseconds { get; private set; }

        [DataMember(Name = "bgraConversionMilliseconds")]
        public double BgraConversionMilliseconds { get; private set; }

        [DataMember(Name = "indexedFrameCount")]
        public long IndexedFrameCount { get; private set; }

        [DataMember(Name = "isGlobalFrameIndexAvailable")]
        public bool IsGlobalFrameIndexAvailable { get; private set; }

        [DataMember(Name = "globalFrameIndexStatus")]
        public string GlobalFrameIndexStatus { get; private set; }

        [DataMember(Name = "videoCodecName")]
        public string VideoCodecName { get; private set; }

        [DataMember(Name = "framesPerSecond")]
        public double FramesPerSecond { get; private set; }
    }

    [DataContract]
    public sealed class DualPaneBudgetHarnessStepMetrics
    {
        [DataMember(Name = "totalSteps")]
        public int TotalSteps { get; set; }

        [DataMember(Name = "cacheHitCount")]
        public int CacheHitCount { get; set; }

        [DataMember(Name = "reconstructionCount")]
        public int ReconstructionCount { get; set; }
    }

    [DataContract]
    public sealed class DualPaneBudgetHarnessCheck
    {
        public DualPaneBudgetHarnessCheck(string name, bool passed, string message)
        {
            Name = name ?? string.Empty;
            Passed = passed;
            Message = message ?? string.Empty;
        }

        [DataMember(Name = "name")]
        public string Name { get; private set; }

        [DataMember(Name = "passed")]
        public bool Passed { get; private set; }

        [DataMember(Name = "message")]
        public string Message { get; private set; }
    }

    [DataContract]
    public sealed class DualPaneBudgetHarnessPairSummary
    {
        public DualPaneBudgetHarnessPairSummary(int checksRun, int passCount, int failCount)
        {
            ChecksRun = checksRun;
            PassCount = passCount;
            FailCount = failCount;
        }

        [DataMember(Name = "checksRun")]
        public int ChecksRun { get; private set; }

        [DataMember(Name = "passCount")]
        public int PassCount { get; private set; }

        [DataMember(Name = "failCount")]
        public int FailCount { get; private set; }
    }

    [DataContract]
    public sealed class DualPaneBudgetHarnessHostSummary
    {
        public DualPaneBudgetHarnessHostSummary(int pairCount, int failedPairCount, int checksRun, int passCount, int failCount)
        {
            PairCount = pairCount;
            FailedPairCount = failedPairCount;
            ChecksRun = checksRun;
            PassCount = passCount;
            FailCount = failCount;
        }

        [DataMember(Name = "pairCount")]
        public int PairCount { get; private set; }

        [DataMember(Name = "failedPairCount")]
        public int FailedPairCount { get; private set; }

        [DataMember(Name = "checksRun")]
        public int ChecksRun { get; private set; }

        [DataMember(Name = "passCount")]
        public int PassCount { get; private set; }

        [DataMember(Name = "failCount")]
        public int FailCount { get; private set; }
    }

    [DataContract]
    public sealed class DualPaneBudgetHarnessSummary
    {
        public DualPaneBudgetHarnessSummary(int pairCount, int failedPairCount, int checksRun, int passCount, int failCount)
        {
            PairCount = pairCount;
            FailedPairCount = failedPairCount;
            ChecksRun = checksRun;
            PassCount = passCount;
            FailCount = failCount;
        }

        [DataMember(Name = "pairCount")]
        public int PairCount { get; private set; }

        [DataMember(Name = "failedPairCount")]
        public int FailedPairCount { get; private set; }

        [DataMember(Name = "checksRun")]
        public int ChecksRun { get; private set; }

        [DataMember(Name = "passCount")]
        public int PassCount { get; private set; }

        [DataMember(Name = "failCount")]
        public int FailCount { get; private set; }
    }
}
