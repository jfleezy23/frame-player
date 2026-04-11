using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using FramePlayer.Services;

namespace FramePlayer.Diagnostics
{
    public static class DecodedFrameBudgetCoordinatorProbe
    {
        private const long MiB = 1024L * 1024L;
        private const int Sample1080pBgraFrameBytes = 1920 * 1080 * 4;

        public static DecodedFrameBudgetCoordinatorProbeReport Run()
        {
            var hostScenarios = new[]
            {
                BuildHostScenario(HostResourceClass.Business16, 16L * 1024L * MiB, 12L * 1024L * MiB),
                BuildHostScenario(HostResourceClass.Workstation32To64, 32L * 1024L * MiB, 24L * 1024L * MiB),
                BuildHostScenario(HostResourceClass.Workstation128Plus, 128L * 1024L * MiB, 96L * 1024L * MiB)
            };

            var checks = hostScenarios
                .SelectMany(scenario => scenario.Checks ?? Array.Empty<DecodedFrameBudgetProbeCheck>())
                .ToArray();

            return new DecodedFrameBudgetCoordinatorProbeReport(
                DateTimeOffset.UtcNow.ToString("o"),
                checks.Length,
                checks.Count(check => check.Passed),
                checks.Count(check => !check.Passed),
                hostScenarios);
        }

        private static DecodedFrameBudgetHostScenarioReport BuildHostScenario(
            HostResourceClass expectedHostClass,
            long totalPhysicalMemoryBytes,
            long availablePhysicalMemoryBytes)
        {
            var scenarios = new[]
            {
                BuildSinglePaneScenario(
                    "SinglePaneCpu",
                    "Single-pane CPU allocation with a 1080p BGRA frame estimate.",
                    expectedHostClass,
                    totalPhysicalMemoryBytes,
                    availablePhysicalMemoryBytes,
                    false,
                    "ffmpeg-cpu"),
                BuildSinglePaneScenario(
                    "SinglePaneGpu",
                    "Single-pane Vulkan allocation with a 1080p BGRA frame estimate.",
                    expectedHostClass,
                    totalPhysicalMemoryBytes,
                    availablePhysicalMemoryBytes,
                    true,
                    "ffmpeg-vulkan"),
                BuildDualPaneBackendAwareScenario(
                    expectedHostClass,
                    totalPhysicalMemoryBytes,
                    availablePhysicalMemoryBytes)
            };

            return new DecodedFrameBudgetHostScenarioReport(
                expectedHostClass.ToString(),
                totalPhysicalMemoryBytes,
                availablePhysicalMemoryBytes,
                scenarios,
                scenarios.SelectMany(scenario => scenario.Checks ?? Array.Empty<DecodedFrameBudgetProbeCheck>()).ToArray());
        }

        private static DecodedFrameBudgetScenarioReport BuildSinglePaneScenario(
            string name,
            string description,
            HostResourceClass expectedHostClass,
            long totalPhysicalMemoryBytes,
            long availablePhysicalMemoryBytes,
            bool gpuActive,
            string actualDecodeBackend)
        {
            var coordinator = new DecodedFrameBudgetCoordinator(totalPhysicalMemoryBytes, availablePhysicalMemoryBytes);
            var allocation = coordinator.UpdatePaneState(
                "pane-primary",
                true,
                gpuActive,
                actualDecodeBackend,
                Sample1080pBgraFrameBytes,
                0,
                null);

            var expectedSessionBudgetBytes = ResolveExpectedSessionBudgetBytes(expectedHostClass, 1);
            var expectedQueueDepth = gpuActive ? 2 : 1;
            var expectedForwardFrames = gpuActive
                ? 1
                : ResolveExpectedSinglePaneCpuForwardTarget(expectedHostClass);
            var expectedBand = gpuActive
                ? DecodedFrameBudgetBand.SinglePaneGpu.ToString()
                : DecodedFrameBudgetBand.SinglePaneCpu.ToString();
            var expectedPreviousFrames = ComputeExpectedPreviousFrames(
                expectedSessionBudgetBytes,
                Sample1080pBgraFrameBytes,
                expectedQueueDepth,
                expectedForwardFrames);

            var checks = new List<DecodedFrameBudgetProbeCheck>
            {
                ExpectEqual(
                    name + "-band",
                    expectedBand,
                    allocation.BudgetBand.ToString(),
                    "The single-pane allocation should land on the expected policy band."),
                ExpectEqual(
                    name + "-host-class",
                    expectedHostClass.ToString(),
                    allocation.HostResourceClass.ToString(),
                    "The simulated host memory should resolve to the expected host class."),
                ExpectEqual(
                    name + "-session-budget",
                    expectedSessionBudgetBytes,
                    allocation.SessionBudgetBytes,
                    "The single-pane session budget should match the host ceiling when pressure is low."),
                ExpectEqual(
                    name + "-pane-budget",
                    expectedSessionBudgetBytes,
                    allocation.PaneBudgetBytes,
                    "Single-pane sessions should grant the full session budget to the active pane."),
                ExpectEqual(
                    name + "-forward-target",
                    expectedForwardFrames,
                    allocation.ForwardFrameTarget,
                    "Single-pane forward reserve should match the CPU/GPU policy target."),
                ExpectEqual(
                    name + "-queue-depth",
                    expectedQueueDepth,
                    allocation.QueueDepth,
                    "Single-pane queue depth should follow the backend-specific operational queue."),
                ExpectEqual(
                    name + "-backend",
                    actualDecodeBackend,
                    allocation.ActualDecodeBackend,
                    "The allocation should track the actual backend that the pane reported."),
                ExpectEqual(
                    name + "-is-gpu-active",
                    gpuActive,
                    allocation.IsGpuActive,
                    "The allocation should preserve whether the pane is actually on GPU."),
                ExpectEqual(
                    name + "-previous-target",
                    expectedPreviousFrames,
                    allocation.PreviousFrameTarget,
                    "Single-pane reverse history should consume the remaining pane budget."),
                ExpectTrue(
                    name + "-reverse-floor",
                    allocation.PreviousFrameTarget >= (gpuActive ? 24 : 16),
                    "Single-pane reverse history should stay at or above the configured floor when memory allows.")
            };

            return new DecodedFrameBudgetScenarioReport(
                name,
                description,
                expectedSessionBudgetBytes,
                new[] { BuildAllocationReport(allocation) },
                checks.ToArray());
        }

        private static DecodedFrameBudgetScenarioReport BuildDualPaneBackendAwareScenario(
            HostResourceClass expectedHostClass,
            long totalPhysicalMemoryBytes,
            long availablePhysicalMemoryBytes)
        {
            var coordinator = new DecodedFrameBudgetCoordinator(totalPhysicalMemoryBytes, availablePhysicalMemoryBytes);
            coordinator.UpdatePaneState(
                "pane-primary",
                true,
                true,
                "ffmpeg-vulkan",
                Sample1080pBgraFrameBytes,
                0,
                null);
            coordinator.UpdatePaneState(
                "pane-compare",
                true,
                false,
                "ffmpeg-cpu",
                Sample1080pBgraFrameBytes,
                0,
                null);

            var primaryAllocation = coordinator.UpdatePaneState(
                "pane-primary",
                true,
                true,
                "ffmpeg-vulkan",
                Sample1080pBgraFrameBytes,
                0,
                null);
            var compareAllocation = coordinator.UpdatePaneState(
                "pane-compare",
                true,
                false,
                "ffmpeg-cpu",
                Sample1080pBgraFrameBytes,
                0,
                null);

            var expectedSessionBudgetBytes = ResolveExpectedSessionBudgetBytes(expectedHostClass, 2);
            var expectedPaneBudgetBytes = expectedSessionBudgetBytes / 2L;
            var expectedGpuPreviousFrames = ComputeExpectedPreviousFrames(
                expectedPaneBudgetBytes,
                Sample1080pBgraFrameBytes,
                2,
                1);
            var expectedCpuPreviousFrames = ComputeExpectedPreviousFrames(
                expectedPaneBudgetBytes,
                Sample1080pBgraFrameBytes,
                1,
                2);

            var checks = new List<DecodedFrameBudgetProbeCheck>
            {
                ExpectEqual(
                    "DualPaneBackendAware-primary-band",
                    DecodedFrameBudgetBand.DualPaneBackendAware.ToString(),
                    primaryAllocation.BudgetBand.ToString(),
                    "The GPU pane should switch to the dual-pane backend-aware band once compare is attached."),
                ExpectEqual(
                    "DualPaneBackendAware-compare-band",
                    DecodedFrameBudgetBand.DualPaneBackendAware.ToString(),
                    compareAllocation.BudgetBand.ToString(),
                    "The CPU pane should switch to the dual-pane backend-aware band once compare is attached."),
                ExpectEqual(
                    "DualPaneBackendAware-session-budget-primary",
                    expectedSessionBudgetBytes,
                    primaryAllocation.SessionBudgetBytes,
                    "Dual-pane sessions should use the host-class dual-pane ceiling when pressure is low."),
                ExpectEqual(
                    "DualPaneBackendAware-session-budget-compare",
                    expectedSessionBudgetBytes,
                    compareAllocation.SessionBudgetBytes,
                    "Both panes should see the same session-wide budget ceiling."),
                ExpectEqual(
                    "DualPaneBackendAware-pane-budget-primary",
                    expectedPaneBudgetBytes,
                    primaryAllocation.PaneBudgetBytes,
                    "Equal frame sizes should split dual-pane reverse growth evenly."),
                ExpectEqual(
                    "DualPaneBackendAware-pane-budget-compare",
                    expectedPaneBudgetBytes,
                    compareAllocation.PaneBudgetBytes,
                    "Equal frame sizes should split dual-pane reverse growth evenly."),
                ExpectEqual(
                    "DualPaneBackendAware-forward-primary",
                    1,
                    primaryAllocation.ForwardFrameTarget,
                    "The GPU pane should keep the reduced dual-pane forward reserve."),
                ExpectEqual(
                    "DualPaneBackendAware-forward-compare",
                    2,
                    compareAllocation.ForwardFrameTarget,
                    "The CPU pane should keep the larger dual-pane forward reserve."),
                ExpectEqual(
                    "DualPaneBackendAware-queue-primary",
                    2,
                    primaryAllocation.QueueDepth,
                    "The GPU pane should keep the GPU operational queue depth in compare."),
                ExpectEqual(
                    "DualPaneBackendAware-queue-compare",
                    1,
                    compareAllocation.QueueDepth,
                    "The CPU pane should keep the CPU operational queue depth in compare."),
                ExpectEqual(
                    "DualPaneBackendAware-previous-primary",
                    expectedGpuPreviousFrames,
                    primaryAllocation.PreviousFrameTarget,
                    "The GPU pane should spend the rest of its pane budget on reverse history."),
                ExpectEqual(
                    "DualPaneBackendAware-previous-compare",
                    expectedCpuPreviousFrames,
                    compareAllocation.PreviousFrameTarget,
                    "The CPU pane should spend the rest of its pane budget on reverse history."),
                ExpectEqual(
                    "DualPaneBackendAware-backend-primary",
                    "ffmpeg-vulkan",
                    primaryAllocation.ActualDecodeBackend,
                    "The compare GPU pane should report its actual Vulkan backend."),
                ExpectEqual(
                    "DualPaneBackendAware-backend-compare",
                    "ffmpeg-cpu",
                    compareAllocation.ActualDecodeBackend,
                    "The compare CPU pane should report its actual CPU backend."),
                ExpectEqual(
                    "DualPaneBackendAware-host-primary",
                    expectedHostClass.ToString(),
                    primaryAllocation.HostResourceClass.ToString(),
                    "The simulated host class should remain visible in the dual-pane GPU allocation."),
                ExpectEqual(
                    "DualPaneBackendAware-host-compare",
                    expectedHostClass.ToString(),
                    compareAllocation.HostResourceClass.ToString(),
                    "The simulated host class should remain visible in the dual-pane CPU allocation."),
                ExpectTrue(
                    "DualPaneBackendAware-reverse-symmetric-growth",
                    primaryAllocation.PreviousFrameTarget == compareAllocation.PreviousFrameTarget,
                    "Equal-size dual panes should grow reverse history symmetrically after floors are assigned."),
                ExpectTrue(
                    "DualPaneBackendAware-gpu-flag",
                    primaryAllocation.IsGpuActive && !compareAllocation.IsGpuActive,
                    "Dual-pane compare sizing must follow the actual active backend for each pane.")
            };

            return new DecodedFrameBudgetScenarioReport(
                "DualPaneBackendAware",
                "Mixed backend compare session with a Vulkan primary pane and CPU compare pane.",
                expectedSessionBudgetBytes,
                new[]
                {
                    BuildAllocationReport(primaryAllocation),
                    BuildAllocationReport(compareAllocation)
                },
                checks.ToArray());
        }

        private static DecodedFrameBudgetAllocationReport BuildAllocationReport(PaneBudgetAllocation allocation)
        {
            return new DecodedFrameBudgetAllocationReport(
                allocation.PaneId,
                allocation.BudgetBand.ToString(),
                allocation.HostResourceClass.ToString(),
                allocation.SessionBudgetBytes,
                allocation.PaneBudgetBytes,
                allocation.PreviousFrameTarget,
                allocation.ForwardFrameTarget,
                allocation.QueueDepth,
                allocation.ActualDecodeBackend,
                allocation.IsGpuActive);
        }

        private static DecodedFrameBudgetProbeCheck ExpectEqual<T>(
            string name,
            T expected,
            T actual,
            string description)
        {
            var passed = EqualityComparer<T>.Default.Equals(expected, actual);
            var message = string.Format(
                "{0} Expected={1}; Actual={2}.",
                description,
                expected,
                actual);
            return new DecodedFrameBudgetProbeCheck(name, passed, message);
        }

        private static DecodedFrameBudgetProbeCheck ExpectTrue(
            string name,
            bool passed,
            string description)
        {
            return new DecodedFrameBudgetProbeCheck(
                name,
                passed,
                description + (passed ? " Passed." : " Failed."));
        }

        private static long ResolveExpectedSessionBudgetBytes(HostResourceClass hostResourceClass, int paneCount)
        {
            switch (hostResourceClass)
            {
                case HostResourceClass.Workstation128Plus:
                    return paneCount > 1 ? 2L * 1024L * MiB : 1536L * MiB;
                case HostResourceClass.Workstation32To64:
                    return paneCount > 1 ? 1536L * MiB : 1024L * MiB;
                case HostResourceClass.Business16:
                default:
                    return paneCount > 1 ? 768L * MiB : 512L * MiB;
            }
        }

        private static int ResolveExpectedSinglePaneCpuForwardTarget(HostResourceClass hostResourceClass)
        {
            switch (hostResourceClass)
            {
                case HostResourceClass.Workstation128Plus:
                    return 2;
                case HostResourceClass.Workstation32To64:
                    return 3;
                case HostResourceClass.Business16:
                default:
                    return 4;
            }
        }

        private static int ComputeExpectedPreviousFrames(
            long paneBudgetBytes,
            int frameBytes,
            int queueDepth,
            int forwardFrames)
        {
            var frameSlots = paneBudgetBytes / Math.Max(1, frameBytes);
            return (int)Math.Max(0L, frameSlots - 1L - Math.Max(0, queueDepth) - Math.Max(0, forwardFrames));
        }
    }

    [DataContract]
    public sealed class DecodedFrameBudgetCoordinatorProbeReport
    {
        public DecodedFrameBudgetCoordinatorProbeReport(
            string generatedAtUtc,
            int checksRun,
            int passCount,
            int failCount,
            DecodedFrameBudgetHostScenarioReport[] hostScenarios)
        {
            GeneratedAtUtc = generatedAtUtc ?? string.Empty;
            ChecksRun = checksRun;
            PassCount = passCount;
            FailCount = failCount;
            HostScenarios = hostScenarios ?? Array.Empty<DecodedFrameBudgetHostScenarioReport>();
        }

        [DataMember(Name = "generatedAtUtc")]
        public string GeneratedAtUtc { get; private set; }

        [DataMember(Name = "checksRun")]
        public int ChecksRun { get; private set; }

        [DataMember(Name = "passCount")]
        public int PassCount { get; private set; }

        [DataMember(Name = "failCount")]
        public int FailCount { get; private set; }

        [DataMember(Name = "hostScenarios")]
        public DecodedFrameBudgetHostScenarioReport[] HostScenarios { get; private set; }
    }

    [DataContract]
    public sealed class DecodedFrameBudgetHostScenarioReport
    {
        public DecodedFrameBudgetHostScenarioReport(
            string hostResourceClass,
            long totalPhysicalMemoryBytes,
            long availablePhysicalMemoryBytes,
            DecodedFrameBudgetScenarioReport[] scenarios,
            DecodedFrameBudgetProbeCheck[] checks)
        {
            HostResourceClass = hostResourceClass ?? string.Empty;
            TotalPhysicalMemoryBytes = totalPhysicalMemoryBytes;
            AvailablePhysicalMemoryBytes = availablePhysicalMemoryBytes;
            Scenarios = scenarios ?? Array.Empty<DecodedFrameBudgetScenarioReport>();
            Checks = checks ?? Array.Empty<DecodedFrameBudgetProbeCheck>();
        }

        [DataMember(Name = "hostResourceClass")]
        public string HostResourceClass { get; private set; }

        [DataMember(Name = "totalPhysicalMemoryBytes")]
        public long TotalPhysicalMemoryBytes { get; private set; }

        [DataMember(Name = "availablePhysicalMemoryBytes")]
        public long AvailablePhysicalMemoryBytes { get; private set; }

        [DataMember(Name = "scenarios")]
        public DecodedFrameBudgetScenarioReport[] Scenarios { get; private set; }

        [DataMember(Name = "checks")]
        public DecodedFrameBudgetProbeCheck[] Checks { get; private set; }
    }

    [DataContract]
    public sealed class DecodedFrameBudgetScenarioReport
    {
        public DecodedFrameBudgetScenarioReport(
            string name,
            string description,
            long expectedSessionBudgetBytes,
            DecodedFrameBudgetAllocationReport[] allocations,
            DecodedFrameBudgetProbeCheck[] checks)
        {
            Name = name ?? string.Empty;
            Description = description ?? string.Empty;
            ExpectedSessionBudgetBytes = expectedSessionBudgetBytes;
            Allocations = allocations ?? Array.Empty<DecodedFrameBudgetAllocationReport>();
            Checks = checks ?? Array.Empty<DecodedFrameBudgetProbeCheck>();
        }

        [DataMember(Name = "name")]
        public string Name { get; private set; }

        [DataMember(Name = "description")]
        public string Description { get; private set; }

        [DataMember(Name = "expectedSessionBudgetBytes")]
        public long ExpectedSessionBudgetBytes { get; private set; }

        [DataMember(Name = "allocations")]
        public DecodedFrameBudgetAllocationReport[] Allocations { get; private set; }

        [DataMember(Name = "checks")]
        public DecodedFrameBudgetProbeCheck[] Checks { get; private set; }
    }

    [DataContract]
    public sealed class DecodedFrameBudgetAllocationReport
    {
        public DecodedFrameBudgetAllocationReport(
            string paneId,
            string budgetBand,
            string hostResourceClass,
            long sessionBudgetBytes,
            long paneBudgetBytes,
            int previousFrameTarget,
            int forwardFrameTarget,
            int queueDepth,
            string actualDecodeBackend,
            bool isGpuActive)
        {
            PaneId = paneId ?? string.Empty;
            BudgetBand = budgetBand ?? string.Empty;
            HostResourceClass = hostResourceClass ?? string.Empty;
            SessionBudgetBytes = sessionBudgetBytes;
            PaneBudgetBytes = paneBudgetBytes;
            PreviousFrameTarget = previousFrameTarget;
            ForwardFrameTarget = forwardFrameTarget;
            QueueDepth = queueDepth;
            ActualDecodeBackend = actualDecodeBackend ?? string.Empty;
            IsGpuActive = isGpuActive;
        }

        [DataMember(Name = "paneId")]
        public string PaneId { get; private set; }

        [DataMember(Name = "budgetBand")]
        public string BudgetBand { get; private set; }

        [DataMember(Name = "hostResourceClass")]
        public string HostResourceClass { get; private set; }

        [DataMember(Name = "sessionBudgetBytes")]
        public long SessionBudgetBytes { get; private set; }

        [DataMember(Name = "paneBudgetBytes")]
        public long PaneBudgetBytes { get; private set; }

        [DataMember(Name = "previousFrameTarget")]
        public int PreviousFrameTarget { get; private set; }

        [DataMember(Name = "forwardFrameTarget")]
        public int ForwardFrameTarget { get; private set; }

        [DataMember(Name = "queueDepth")]
        public int QueueDepth { get; private set; }

        [DataMember(Name = "actualDecodeBackend")]
        public string ActualDecodeBackend { get; private set; }

        [DataMember(Name = "isGpuActive")]
        public bool IsGpuActive { get; private set; }
    }

    [DataContract]
    public sealed class DecodedFrameBudgetProbeCheck
    {
        public DecodedFrameBudgetProbeCheck(
            string name,
            bool passed,
            string message)
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
}
