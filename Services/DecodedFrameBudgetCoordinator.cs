using System;
using System.Collections.Generic;
using System.Linq;
using FramePlayer.Engines.FFmpeg;

namespace FramePlayer.Services
{
    internal enum DecodedFrameBudgetBand
    {
        SinglePaneCpu = 0,
        SinglePaneGpu = 1,
        DualPaneBackendAware = 2
    }

    internal enum HostResourceClass
    {
        Business16 = 0,
        Workstation32To64 = 1,
        Workstation128Plus = 2
    }

    internal sealed class PaneBudgetAllocation
    {
        public PaneBudgetAllocation(
            string paneId,
            DecodedFrameBudgetBand budgetBand,
            HostResourceClass hostResourceClass,
            long sessionBudgetBytes,
            long paneBudgetBytes,
            int previousFrameTarget,
            int forwardFrameTarget,
            int queueDepth,
            string actualDecodeBackend,
            bool isGpuActive)
        {
            PaneId = paneId ?? string.Empty;
            BudgetBand = budgetBand;
            HostResourceClass = hostResourceClass;
            SessionBudgetBytes = Math.Max(0L, sessionBudgetBytes);
            PaneBudgetBytes = Math.Max(0L, paneBudgetBytes);
            PreviousFrameTarget = Math.Max(0, previousFrameTarget);
            ForwardFrameTarget = Math.Max(0, forwardFrameTarget);
            QueueDepth = Math.Max(0, queueDepth);
            ActualDecodeBackend = actualDecodeBackend ?? string.Empty;
            IsGpuActive = isGpuActive;
        }

        public string PaneId { get; }

        public DecodedFrameBudgetBand BudgetBand { get; }

        public HostResourceClass HostResourceClass { get; }

        public long SessionBudgetBytes { get; }

        public long PaneBudgetBytes { get; }

        public int PreviousFrameTarget { get; }

        public int ForwardFrameTarget { get; }

        public int QueueDepth { get; }

        public string ActualDecodeBackend { get; }

        public bool IsGpuActive { get; }

        public static PaneBudgetAllocation CreateDefault(string paneId, HostResourceClass hostResourceClass)
        {
            return new PaneBudgetAllocation(
                paneId,
                DecodedFrameBudgetBand.SinglePaneCpu,
                hostResourceClass,
                0L,
                0L,
                10,
                1,
                1,
                "ffmpeg-cpu",
                false);
        }

        public bool IsEquivalentTo(PaneBudgetAllocation other)
        {
            if (other == null)
            {
                return false;
            }

            return string.Equals(PaneId, other.PaneId, StringComparison.Ordinal) &&
                   BudgetBand == other.BudgetBand &&
                   HostResourceClass == other.HostResourceClass &&
                   SessionBudgetBytes == other.SessionBudgetBytes &&
                   PaneBudgetBytes == other.PaneBudgetBytes &&
                   PreviousFrameTarget == other.PreviousFrameTarget &&
                   ForwardFrameTarget == other.ForwardFrameTarget &&
                   QueueDepth == other.QueueDepth &&
                   string.Equals(ActualDecodeBackend, other.ActualDecodeBackend, StringComparison.Ordinal) &&
                   IsGpuActive == other.IsGpuActive;
        }
    }

    internal sealed class PaneBudgetAllocationChangedEventArgs : EventArgs
    {
        public PaneBudgetAllocationChangedEventArgs(PaneBudgetAllocation allocation)
        {
            Allocation = allocation ?? throw new ArgumentNullException(nameof(allocation));
        }

        public PaneBudgetAllocation Allocation { get; }
    }

    internal sealed class DecodedFrameBudgetCoordinator
    {
        private const long MiB = 1024L * 1024L;
        private const long UnknownFrameEstimateBytes = 8L * MiB;
        private const int MinimumProtectedPreviousFrames = 4;

        private readonly object _sync = new object();
        private readonly Dictionary<string, PaneBudgetState> _paneStates =
            new Dictionary<string, PaneBudgetState>(StringComparer.Ordinal);
        private readonly long? _availablePhysicalMemoryBytesOverride;
        private readonly HostResourceClass _hostResourceClass;

        public DecodedFrameBudgetCoordinator()
            : this(null, null)
        {
        }

        internal DecodedFrameBudgetCoordinator(
            long? totalPhysicalMemoryBytesOverride,
            long? availablePhysicalMemoryBytesOverride)
        {
            _availablePhysicalMemoryBytesOverride = availablePhysicalMemoryBytesOverride;

            long totalPhysicalMemoryBytes;
            if (totalPhysicalMemoryBytesOverride.HasValue && totalPhysicalMemoryBytesOverride.Value > 0L)
            {
                totalPhysicalMemoryBytes = totalPhysicalMemoryBytesOverride.Value;
            }
            else if (!FfmpegNativeHelpers.TryGetTotalPhysicalMemoryBytes(out totalPhysicalMemoryBytes) ||
                     totalPhysicalMemoryBytes <= 0L)
            {
                totalPhysicalMemoryBytes = 16L * 1024L * MiB;
            }

            _hostResourceClass = DetermineHostResourceClass(totalPhysicalMemoryBytes);
        }

        public event EventHandler<PaneBudgetAllocationChangedEventArgs> AllocationChanged;

        public HostResourceClass HostResourceClass
        {
            get { return _hostResourceClass; }
        }

        public void RegisterPane(string paneId)
        {
            var normalizedPaneId = NormalizePaneId(paneId);
            lock (_sync)
            {
                if (_paneStates.ContainsKey(normalizedPaneId))
                {
                    return;
                }

                _paneStates.Add(normalizedPaneId, new PaneBudgetState(
                    normalizedPaneId,
                    PaneBudgetAllocation.CreateDefault(normalizedPaneId, _hostResourceClass)));
            }
        }

        public void UnregisterPane(string paneId)
        {
            List<PaneBudgetAllocation> changedAllocations;
            var normalizedPaneId = NormalizePaneId(paneId);
            lock (_sync)
            {
                if (!_paneStates.Remove(normalizedPaneId))
                {
                    return;
                }

                changedAllocations = RecalculateAllocationsLocked();
            }

            NotifyAllocationChanges(changedAllocations);
        }

        public PaneBudgetAllocation UpdatePaneState(
            string paneId,
            bool isOpen,
            bool gpuActive,
            string actualDecodeBackend,
            int approximateFrameBytes,
            int operationalQueueDepth,
            int? sessionBudgetOverrideMegabytes)
        {
            List<PaneBudgetAllocation> changedAllocations;
            PaneBudgetAllocation allocation;
            var normalizedPaneId = NormalizePaneId(paneId);

            lock (_sync)
            {
                PaneBudgetState state;
                if (!_paneStates.TryGetValue(normalizedPaneId, out state))
                {
                    state = new PaneBudgetState(
                        normalizedPaneId,
                        PaneBudgetAllocation.CreateDefault(normalizedPaneId, _hostResourceClass));
                    _paneStates.Add(normalizedPaneId, state);
                }

                state.IsOpen = isOpen;
                state.IsGpuActive = gpuActive;
                state.ActualDecodeBackend = string.IsNullOrWhiteSpace(actualDecodeBackend)
                    ? (gpuActive ? "ffmpeg-vulkan" : "ffmpeg-cpu")
                    : actualDecodeBackend;
                state.ApproximateFrameBytes = approximateFrameBytes;
                state.OperationalQueueDepth = Math.Max(0, operationalQueueDepth);
                state.SessionBudgetOverrideMegabytes = sessionBudgetOverrideMegabytes;

                changedAllocations = RecalculateAllocationsLocked();
                allocation = state.CurrentAllocation;
            }

            NotifyAllocationChanges(changedAllocations);
            return allocation ?? PaneBudgetAllocation.CreateDefault(normalizedPaneId, _hostResourceClass);
        }

        private static string NormalizePaneId(string paneId)
        {
            return string.IsNullOrWhiteSpace(paneId)
                ? "pane-primary"
                : paneId.Trim();
        }

        private void NotifyAllocationChanges(IEnumerable<PaneBudgetAllocation> allocations)
        {
            var handler = AllocationChanged;
            if (handler == null || allocations == null)
            {
                return;
            }

            foreach (var allocation in allocations)
            {
                if (allocation == null)
                {
                    continue;
                }

                handler(this, new PaneBudgetAllocationChangedEventArgs(allocation));
            }
        }

        private List<PaneBudgetAllocation> RecalculateAllocationsLocked()
        {
            var changedAllocations = new List<PaneBudgetAllocation>();
            var openStates = _paneStates.Values
                .Where(state => state.IsOpen)
                .OrderBy(state => state.PaneId, StringComparer.Ordinal)
                .ToArray();

            if (openStates.Length == 0)
            {
                foreach (var state in _paneStates.Values)
                {
                    if (UpdateStateAllocationLocked(state, PaneBudgetAllocation.CreateDefault(state.PaneId, _hostResourceClass)))
                    {
                        changedAllocations.Add(state.CurrentAllocation);
                    }
                }

                return changedAllocations;
            }

            var sessionBudgetBytes = ResolveSessionBudgetBytes(openStates);
            if (openStates.Length == 1)
            {
                var allocation = BuildSinglePaneAllocation(openStates[0], sessionBudgetBytes);
                if (UpdateStateAllocationLocked(openStates[0], allocation))
                {
                    changedAllocations.Add(allocation);
                }
            }
            else
            {
                foreach (var allocation in BuildDualPaneAllocations(openStates, sessionBudgetBytes))
                {
                    PaneBudgetState state;
                    if (_paneStates.TryGetValue(allocation.PaneId, out state) &&
                        UpdateStateAllocationLocked(state, allocation))
                    {
                        changedAllocations.Add(allocation);
                    }
                }
            }

            foreach (var state in _paneStates.Values.Where(state => !state.IsOpen))
            {
                if (UpdateStateAllocationLocked(state, PaneBudgetAllocation.CreateDefault(state.PaneId, _hostResourceClass)))
                {
                    changedAllocations.Add(state.CurrentAllocation);
                }
            }

            return changedAllocations;
        }

        private static bool UpdateStateAllocationLocked(PaneBudgetState state, PaneBudgetAllocation allocation)
        {
            if (state == null)
            {
                return false;
            }

            if (state.CurrentAllocation != null && state.CurrentAllocation.IsEquivalentTo(allocation))
            {
                return false;
            }

            state.CurrentAllocation = allocation;
            return true;
        }

        private long ResolveSessionBudgetBytes(IReadOnlyList<PaneBudgetState> openStates)
        {
            if (openStates == null || openStates.Count == 0)
            {
                return 0L;
            }

            var overrideMegabytes = openStates
                .Where(state => state.SessionBudgetOverrideMegabytes.HasValue)
                .Select(state => state.SessionBudgetOverrideMegabytes.Value)
                .DefaultIfEmpty(0)
                .Max();
            if (overrideMegabytes > 0)
            {
                return overrideMegabytes * MiB;
            }

            var ceilingBytes = ResolveSessionBudgetCeilingBytes(_hostResourceClass, openStates.Count);
            long availablePhysicalMemoryBytes;
            if (_availablePhysicalMemoryBytesOverride.HasValue && _availablePhysicalMemoryBytesOverride.Value > 0L)
            {
                availablePhysicalMemoryBytes = _availablePhysicalMemoryBytesOverride.Value;
            }
            else if (!FfmpegNativeHelpers.TryGetAvailablePhysicalMemoryBytes(out availablePhysicalMemoryBytes) ||
                     availablePhysicalMemoryBytes <= 0L)
            {
                return ceilingBytes;
            }

            var pressureLimitedBudgetBytes = Math.Max(64L * MiB, availablePhysicalMemoryBytes / 6L);
            return Math.Max(64L * MiB, Math.Min(ceilingBytes, pressureLimitedBudgetBytes));
        }

        private PaneBudgetAllocation BuildSinglePaneAllocation(PaneBudgetState state, long sessionBudgetBytes)
        {
            var isGpuActive = state != null && state.IsGpuActive;
            var queueDepth = state != null && state.OperationalQueueDepth > 0
                ? state.OperationalQueueDepth
                : ResolveQueueDepth(isGpuActive);
            var frameBytes = ResolveFrameBytes(state);
            var forwardTarget = ResolveSinglePaneForwardTarget(_hostResourceClass, isGpuActive);
            var reverseTarget = isGpuActive ? 24 : 16;

            ReduceTargetsToFitBudget(
                sessionBudgetBytes,
                frameBytes,
                queueDepth,
                ref forwardTarget,
                ref reverseTarget);

            var previousFrames = ComputePreviousFramesFromBudget(
                sessionBudgetBytes,
                frameBytes,
                queueDepth,
                forwardTarget);

            return new PaneBudgetAllocation(
                state != null ? state.PaneId : string.Empty,
                isGpuActive ? DecodedFrameBudgetBand.SinglePaneGpu : DecodedFrameBudgetBand.SinglePaneCpu,
                _hostResourceClass,
                sessionBudgetBytes,
                sessionBudgetBytes,
                previousFrames,
                forwardTarget,
                queueDepth,
                ResolveActualBackend(state),
                isGpuActive);
        }

        private IReadOnlyList<PaneBudgetAllocation> BuildDualPaneAllocations(
            IReadOnlyList<PaneBudgetState> openStates,
            long sessionBudgetBytes)
        {
            var paneCount = openStates.Count;
            var forwardTargets = new int[paneCount];
            var reverseTargets = new int[paneCount];
            var queueDepths = new int[paneCount];
            var frameBytes = new long[paneCount];
            var baseBudgetBytes = new long[paneCount];

            for (var index = 0; index < paneCount; index++)
            {
                var state = openStates[index];
                forwardTargets[index] = state.IsGpuActive ? 1 : 2;
                reverseTargets[index] = 12;
                queueDepths[index] = state.OperationalQueueDepth > 0
                    ? state.OperationalQueueDepth
                    : ResolveQueueDepth(state.IsGpuActive);
                frameBytes[index] = ResolveFrameBytes(state);
            }

            var requiredBytes = ComputeRequiredBudgetBytes(frameBytes, queueDepths, forwardTargets, reverseTargets);
            while (requiredBytes > sessionBudgetBytes && forwardTargets.Any(target => target > 0))
            {
                for (var index = 0; index < paneCount && requiredBytes > sessionBudgetBytes; index++)
                {
                    if (forwardTargets[index] <= 0)
                    {
                        continue;
                    }

                    forwardTargets[index]--;
                    requiredBytes = ComputeRequiredBudgetBytes(frameBytes, queueDepths, forwardTargets, reverseTargets);
                }
            }

            while (requiredBytes > sessionBudgetBytes && reverseTargets.Any(target => target > MinimumProtectedPreviousFrames))
            {
                for (var index = 0; index < paneCount && requiredBytes > sessionBudgetBytes; index++)
                {
                    if (reverseTargets[index] <= MinimumProtectedPreviousFrames)
                    {
                        continue;
                    }

                    reverseTargets[index]--;
                    requiredBytes = ComputeRequiredBudgetBytes(frameBytes, queueDepths, forwardTargets, reverseTargets);
                }
            }

            long allocatedBytes = 0L;
            for (var index = 0; index < paneCount; index++)
            {
                baseBudgetBytes[index] = ComputeRequiredBudgetBytes(
                    frameBytes[index],
                    queueDepths[index],
                    forwardTargets[index],
                    reverseTargets[index]);
                allocatedBytes += baseBudgetBytes[index];
            }

            var remainingBytes = Math.Max(0L, sessionBudgetBytes - allocatedBytes);
            var extraPerPaneBytes = paneCount > 0 ? remainingBytes / paneCount : 0L;
            var leftoverBytes = paneCount > 0 ? remainingBytes % paneCount : 0L;
            var allocations = new List<PaneBudgetAllocation>(paneCount);
            for (var index = 0; index < paneCount; index++)
            {
                var paneBudgetBytes = baseBudgetBytes[index] + extraPerPaneBytes + (index < leftoverBytes ? 1L : 0L);
                var previousFrames = ComputePreviousFramesFromBudget(
                    paneBudgetBytes,
                    frameBytes[index],
                    queueDepths[index],
                    forwardTargets[index]);

                allocations.Add(new PaneBudgetAllocation(
                    openStates[index].PaneId,
                    DecodedFrameBudgetBand.DualPaneBackendAware,
                    _hostResourceClass,
                    sessionBudgetBytes,
                    paneBudgetBytes,
                    previousFrames,
                    forwardTargets[index],
                    queueDepths[index],
                    ResolveActualBackend(openStates[index]),
                    openStates[index].IsGpuActive));
            }

            return allocations;
        }

        private static long ResolveSessionBudgetCeilingBytes(HostResourceClass hostResourceClass, int openPaneCount)
        {
            switch (hostResourceClass)
            {
                case HostResourceClass.Workstation128Plus:
                    return openPaneCount > 1
                        ? 2L * 1024L * MiB
                        : 1536L * MiB;
                case HostResourceClass.Workstation32To64:
                    return openPaneCount > 1
                        ? 1536L * MiB
                        : 1024L * MiB;
                case HostResourceClass.Business16:
                default:
                    return openPaneCount > 1
                        ? 768L * MiB
                        : 512L * MiB;
            }
        }

        private static HostResourceClass DetermineHostResourceClass(long totalPhysicalMemoryBytes)
        {
            if (totalPhysicalMemoryBytes >= 96L * 1024L * MiB)
            {
                return HostResourceClass.Workstation128Plus;
            }

            if (totalPhysicalMemoryBytes >= 24L * 1024L * MiB)
            {
                return HostResourceClass.Workstation32To64;
            }

            return HostResourceClass.Business16;
        }

        private static int ResolveQueueDepth(bool gpuActive)
        {
            return gpuActive ? 2 : 1;
        }

        private static int ResolveSinglePaneForwardTarget(HostResourceClass hostResourceClass, bool isGpuActive)
        {
            if (isGpuActive)
            {
                return 1;
            }

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

        private static long ResolveFrameBytes(PaneBudgetState state)
        {
            if (state == null || state.ApproximateFrameBytes <= 0)
            {
                return UnknownFrameEstimateBytes;
            }

            return Math.Max(1L, state.ApproximateFrameBytes);
        }

        private static string ResolveActualBackend(PaneBudgetState state)
        {
            if (state == null)
            {
                return "ffmpeg-cpu";
            }

            if (!string.IsNullOrWhiteSpace(state.ActualDecodeBackend))
            {
                return state.ActualDecodeBackend;
            }

            return state.IsGpuActive ? "ffmpeg-vulkan" : "ffmpeg-cpu";
        }

        private static void ReduceTargetsToFitBudget(
            long budgetBytes,
            long frameBytes,
            int queueDepth,
            ref int forwardTarget,
            ref int reverseTarget)
        {
            var requiredBytes = ComputeRequiredBudgetBytes(frameBytes, queueDepth, forwardTarget, reverseTarget);
            while (requiredBytes > budgetBytes && forwardTarget > 0)
            {
                forwardTarget--;
                requiredBytes = ComputeRequiredBudgetBytes(frameBytes, queueDepth, forwardTarget, reverseTarget);
            }

            while (requiredBytes > budgetBytes && reverseTarget > MinimumProtectedPreviousFrames)
            {
                reverseTarget--;
                requiredBytes = ComputeRequiredBudgetBytes(frameBytes, queueDepth, forwardTarget, reverseTarget);
            }
        }

        private static long ComputeRequiredBudgetBytes(
            IReadOnlyList<long> frameBytes,
            IReadOnlyList<int> queueDepths,
            IReadOnlyList<int> forwardTargets,
            IReadOnlyList<int> reverseTargets)
        {
            long totalBytes = 0L;
            for (var index = 0; index < frameBytes.Count; index++)
            {
                totalBytes += ComputeRequiredBudgetBytes(
                    frameBytes[index],
                    queueDepths[index],
                    forwardTargets[index],
                    reverseTargets[index]);
            }

            return totalBytes;
        }

        private static long ComputeRequiredBudgetBytes(
            long frameBytes,
            int queueDepth,
            int forwardTarget,
            int reverseTarget)
        {
            var frameCount = 1L + Math.Max(0, queueDepth) + Math.Max(0, forwardTarget) + Math.Max(0, reverseTarget);
            return frameCount * Math.Max(1L, frameBytes);
        }

        private static int ComputePreviousFramesFromBudget(
            long paneBudgetBytes,
            long frameBytes,
            int queueDepth,
            int forwardTarget)
        {
            if (frameBytes <= 0L)
            {
                return 0;
            }

            var availableFrameSlots = (paneBudgetBytes / frameBytes) - 1L - Math.Max(0, queueDepth) - Math.Max(0, forwardTarget);
            return (int)Math.Max(0L, Math.Min(int.MaxValue, availableFrameSlots));
        }

        private sealed class PaneBudgetState
        {
            public PaneBudgetState(string paneId, PaneBudgetAllocation currentAllocation)
            {
                PaneId = paneId ?? string.Empty;
                CurrentAllocation = currentAllocation;
                ActualDecodeBackend = "ffmpeg-cpu";
            }

            public string PaneId { get; }

            public bool IsOpen { get; set; }

            public bool IsGpuActive { get; set; }

            public string ActualDecodeBackend { get; set; }

            public int ApproximateFrameBytes { get; set; }

            public int OperationalQueueDepth { get; set; }

            public int? SessionBudgetOverrideMegabytes { get; set; }

            public PaneBudgetAllocation CurrentAllocation { get; set; }
        }
    }
}
