using System;
using System.Collections.Generic;
using FramePlayer.Services;
using Xunit;

namespace FramePlayer.Core.Tests
{
    public sealed class DecodedFrameBudgetCoordinatorTests
    {
        private const long MiB = 1024L * 1024L;
        private const long GiB = 1024L * MiB;

        [Fact]
        public void SinglePaneAllocation_UsesStableCpuAndGpuPolicies()
        {
            var coordinator = new DecodedFrameBudgetCoordinator(
                totalPhysicalMemoryBytesOverride: 16L * GiB,
                availablePhysicalMemoryBytesOverride: 8L * GiB);
            var changes = new List<PaneBudgetAllocation>();
            coordinator.AllocationChanged += (_, args) => changes.Add(args.Allocation);

            coordinator.RegisterPane(" pane-primary ");
            coordinator.RegisterPane("pane-primary");
            var closed = coordinator.UpdatePaneState(
                "pane-primary",
                isOpen: false,
                gpuActive: false,
                actualDecodeBackend: string.Empty,
                approximateFrameBytes: 0,
                operationalQueueDepth: -1,
                sessionBudgetOverrideMegabytes: null);
            Assert.Equal(DecodedFrameBudgetBand.SinglePaneCpu, closed.BudgetBand);
            Assert.Equal(0L, closed.SessionBudgetBytes);

            var cpu = coordinator.UpdatePaneState(
                "pane-primary",
                isOpen: true,
                gpuActive: false,
                actualDecodeBackend: DecodeBackendNames.Cpu,
                approximateFrameBytes: 8 * (int)MiB,
                operationalQueueDepth: 0,
                sessionBudgetOverrideMegabytes: 256);
            Assert.Equal(HostResourceClass.Business16, coordinator.HostResourceClass);
            Assert.Equal(DecodedFrameBudgetBand.SinglePaneCpu, cpu.BudgetBand);
            Assert.Equal(256L * MiB, cpu.SessionBudgetBytes);
            Assert.Equal(256L * MiB, cpu.PaneBudgetBytes);
            Assert.Equal(4, cpu.ForwardFrameTarget);
            Assert.Equal(1, cpu.QueueDepth);
            Assert.Equal(DecodeBackendNames.Cpu, cpu.ActualDecodeBackend);
            Assert.False(cpu.IsGpuActive);
            Assert.True(cpu.PreviousFrameTarget >= 4);

            var changeCount = changes.Count;
            var equivalentCpu = coordinator.UpdatePaneState(
                "pane-primary",
                isOpen: true,
                gpuActive: false,
                actualDecodeBackend: DecodeBackendNames.Cpu,
                approximateFrameBytes: 8 * (int)MiB,
                operationalQueueDepth: 0,
                sessionBudgetOverrideMegabytes: 256);
            Assert.True(cpu.IsEquivalentTo(equivalentCpu));
            Assert.Equal(changeCount, changes.Count);

            var gpu = coordinator.UpdatePaneState(
                "pane-primary",
                isOpen: true,
                gpuActive: true,
                actualDecodeBackend: null,
                approximateFrameBytes: 16 * (int)MiB,
                operationalQueueDepth: 0,
                sessionBudgetOverrideMegabytes: 128);
            Assert.Equal(DecodedFrameBudgetBand.SinglePaneGpu, gpu.BudgetBand);
            Assert.Equal(0, gpu.ForwardFrameTarget);
            Assert.Equal(2, gpu.QueueDepth);
            Assert.Equal(DecodeBackendNames.Vulkan, gpu.ActualDecodeBackend);
            Assert.True(gpu.IsGpuActive);
            Assert.True(changes.Count > changeCount);

            coordinator.UnregisterPane("missing");
            coordinator.UnregisterPane("pane-primary");
        }

        [Fact]
        public void DualPaneAllocation_DegradesTargetsWithinSharedBudget()
        {
            var coordinator = new DecodedFrameBudgetCoordinator(
                totalPhysicalMemoryBytesOverride: 128L * GiB,
                availablePhysicalMemoryBytesOverride: 4L * GiB);
            var changes = new List<PaneBudgetAllocation>();
            coordinator.AllocationChanged += (_, args) => changes.Add(args.Allocation);

            var primary = coordinator.UpdatePaneState(
                "pane-primary",
                isOpen: true,
                gpuActive: false,
                actualDecodeBackend: DecodeBackendNames.Cpu,
                approximateFrameBytes: 32 * (int)MiB,
                operationalQueueDepth: 1,
                sessionBudgetOverrideMegabytes: 512);
            var secondary = coordinator.UpdatePaneState(
                "pane-secondary",
                isOpen: true,
                gpuActive: true,
                actualDecodeBackend: string.Empty,
                approximateFrameBytes: 32 * (int)MiB,
                operationalQueueDepth: 2,
                sessionBudgetOverrideMegabytes: 512);
            primary = coordinator.UpdatePaneState(
                "pane-primary",
                isOpen: true,
                gpuActive: false,
                actualDecodeBackend: DecodeBackendNames.Cpu,
                approximateFrameBytes: 32 * (int)MiB,
                operationalQueueDepth: 1,
                sessionBudgetOverrideMegabytes: 512);

            Assert.Equal(HostResourceClass.Workstation128Plus, coordinator.HostResourceClass);
            Assert.Equal(DecodedFrameBudgetBand.DualPaneBackendAware, primary.BudgetBand);
            Assert.Equal(DecodedFrameBudgetBand.DualPaneBackendAware, secondary.BudgetBand);
            Assert.Equal(512L * MiB, primary.SessionBudgetBytes);
            Assert.Equal(primary.SessionBudgetBytes, secondary.SessionBudgetBytes);
            Assert.True(primary.PaneBudgetBytes > 0L);
            Assert.True(secondary.PaneBudgetBytes > 0L);
            Assert.True(primary.PaneBudgetBytes + secondary.PaneBudgetBytes <= primary.SessionBudgetBytes);
            Assert.Equal(0, primary.ForwardFrameTarget);
            Assert.Equal(0, secondary.ForwardFrameTarget);
            Assert.True(primary.PreviousFrameTarget >= 4);
            Assert.True(secondary.PreviousFrameTarget >= 4);
            Assert.Equal(DecodeBackendNames.Vulkan, secondary.ActualDecodeBackend);

            var closed = coordinator.UpdatePaneState(
                "pane-closed",
                isOpen: false,
                gpuActive: false,
                actualDecodeBackend: null,
                approximateFrameBytes: -1,
                operationalQueueDepth: 0,
                sessionBudgetOverrideMegabytes: null);
            Assert.Equal(0L, closed.PaneBudgetBytes);
            Assert.Equal(10, closed.PreviousFrameTarget);

            coordinator.UnregisterPane("pane-secondary");
            primary = coordinator.UpdatePaneState(
                "pane-primary",
                isOpen: true,
                gpuActive: false,
                actualDecodeBackend: DecodeBackendNames.Cpu,
                approximateFrameBytes: 32 * (int)MiB,
                operationalQueueDepth: 1,
                sessionBudgetOverrideMegabytes: 512);
            Assert.Equal(DecodedFrameBudgetBand.SinglePaneCpu, primary.BudgetBand);
            Assert.NotEmpty(changes);
        }

        [Fact]
        public void SinglePaneAllocation_FailsClosedWhenOperationalQueueCannotFit()
        {
            var coordinator = new DecodedFrameBudgetCoordinator(
                totalPhysicalMemoryBytesOverride: 16L * GiB,
                availablePhysicalMemoryBytesOverride: 64L * MiB);

            var allocation = coordinator.UpdatePaneState(
                "pane-primary",
                isOpen: true,
                gpuActive: false,
                actualDecodeBackend: DecodeBackendNames.Cpu,
                approximateFrameBytes: 64 * (int)MiB,
                operationalQueueDepth: 2,
                sessionBudgetOverrideMegabytes: null);

            Assert.Equal((64L * MiB) - 1L, allocation.PaneBudgetBytes);
            Assert.Equal(0, allocation.ForwardFrameTarget);
            Assert.Equal(0, allocation.PreviousFrameTarget);
            Assert.Equal(2, allocation.QueueDepth);
        }

        [Fact]
        public void DualPaneAllocation_NeverExceedsConstrainedSessionBudget()
        {
            var coordinator = new DecodedFrameBudgetCoordinator(
                totalPhysicalMemoryBytesOverride: 16L * GiB,
                availablePhysicalMemoryBytesOverride: 64L * MiB);

            coordinator.UpdatePaneState(
                "pane-primary",
                isOpen: true,
                gpuActive: false,
                actualDecodeBackend: DecodeBackendNames.Cpu,
                approximateFrameBytes: 32 * (int)MiB,
                operationalQueueDepth: 1,
                sessionBudgetOverrideMegabytes: null);
            var secondary = coordinator.UpdatePaneState(
                "pane-secondary",
                isOpen: true,
                gpuActive: true,
                actualDecodeBackend: DecodeBackendNames.Vulkan,
                approximateFrameBytes: 32 * (int)MiB,
                operationalQueueDepth: 2,
                sessionBudgetOverrideMegabytes: null);
            var primary = coordinator.UpdatePaneState(
                "pane-primary",
                isOpen: true,
                gpuActive: false,
                actualDecodeBackend: DecodeBackendNames.Cpu,
                approximateFrameBytes: 32 * (int)MiB,
                operationalQueueDepth: 1,
                sessionBudgetOverrideMegabytes: null);

            Assert.Equal(64L * MiB, primary.SessionBudgetBytes);
            Assert.Equal(primary.SessionBudgetBytes, secondary.SessionBudgetBytes);
            Assert.Equal(0, primary.ForwardFrameTarget);
            Assert.Equal(0, secondary.ForwardFrameTarget);
            Assert.Equal(0, primary.PreviousFrameTarget);
            Assert.Equal(0, secondary.PreviousFrameTarget);
            Assert.Equal(1, primary.QueueDepth);
            Assert.Equal(2, secondary.QueueDepth);
            Assert.True(primary.PaneBudgetBytes > 0L);
            Assert.True(secondary.PaneBudgetBytes > 0L);
            Assert.True(primary.PaneBudgetBytes < 32L * MiB);
            Assert.True(secondary.PaneBudgetBytes < 32L * MiB);
            Assert.True(primary.PaneBudgetBytes + secondary.PaneBudgetBytes <= primary.SessionBudgetBytes);
        }

        [Fact]
        public void DualPaneAllocation_FailsClosedForHeterogeneousFramesWhenQueuesCannotFit()
        {
            var coordinator = new DecodedFrameBudgetCoordinator(
                totalPhysicalMemoryBytesOverride: 16L * GiB,
                availablePhysicalMemoryBytesOverride: 64L * MiB);

            coordinator.UpdatePaneState(
                "pane-primary",
                isOpen: true,
                gpuActive: false,
                actualDecodeBackend: DecodeBackendNames.Cpu,
                approximateFrameBytes: 48 * (int)MiB,
                operationalQueueDepth: 1,
                sessionBudgetOverrideMegabytes: null);
            var secondary = coordinator.UpdatePaneState(
                "pane-secondary",
                isOpen: true,
                gpuActive: true,
                actualDecodeBackend: DecodeBackendNames.Vulkan,
                approximateFrameBytes: 16 * (int)MiB,
                operationalQueueDepth: 2,
                sessionBudgetOverrideMegabytes: null);
            var primary = coordinator.UpdatePaneState(
                "pane-primary",
                isOpen: true,
                gpuActive: false,
                actualDecodeBackend: DecodeBackendNames.Cpu,
                approximateFrameBytes: 48 * (int)MiB,
                operationalQueueDepth: 1,
                sessionBudgetOverrideMegabytes: null);

            Assert.True(primary.PaneBudgetBytes > 0L);
            Assert.True(secondary.PaneBudgetBytes > 0L);
            Assert.True(primary.PaneBudgetBytes < 48L * MiB);
            Assert.True(secondary.PaneBudgetBytes < 16L * MiB);
            Assert.Equal(1, primary.QueueDepth);
            Assert.Equal(2, secondary.QueueDepth);
            Assert.True(primary.PaneBudgetBytes + secondary.PaneBudgetBytes <= primary.SessionBudgetBytes);
        }

        [Fact]
        public void DualPaneAllocation_ClampsExtremeQueueDepthBeforeAccounting()
        {
            var coordinator = new DecodedFrameBudgetCoordinator(
                totalPhysicalMemoryBytesOverride: 16L * GiB,
                availablePhysicalMemoryBytesOverride: 8L * GiB);

            coordinator.UpdatePaneState(
                "pane-primary",
                isOpen: true,
                gpuActive: false,
                actualDecodeBackend: DecodeBackendNames.Cpu,
                approximateFrameBytes: int.MaxValue,
                operationalQueueDepth: int.MaxValue,
                sessionBudgetOverrideMegabytes: 1_000_000);
            var secondary = coordinator.UpdatePaneState(
                "pane-secondary",
                isOpen: true,
                gpuActive: true,
                actualDecodeBackend: DecodeBackendNames.Vulkan,
                approximateFrameBytes: int.MaxValue,
                operationalQueueDepth: int.MaxValue,
                sessionBudgetOverrideMegabytes: 1_000_000);
            var primary = coordinator.UpdatePaneState(
                "pane-primary",
                isOpen: true,
                gpuActive: false,
                actualDecodeBackend: DecodeBackendNames.Cpu,
                approximateFrameBytes: int.MaxValue,
                operationalQueueDepth: int.MaxValue,
                sessionBudgetOverrideMegabytes: 1_000_000);

            Assert.Equal(2, primary.QueueDepth);
            Assert.Equal(2, secondary.QueueDepth);
            Assert.True(primary.PaneBudgetBytes > 0L);
            Assert.True(secondary.PaneBudgetBytes > 0L);
            Assert.True(primary.PaneBudgetBytes <= primary.SessionBudgetBytes);
            Assert.True(secondary.PaneBudgetBytes <= secondary.SessionBudgetBytes);
            Assert.True(primary.PaneBudgetBytes + secondary.PaneBudgetBytes <= primary.SessionBudgetBytes);
        }

        [Fact]
        public void DualPaneAllocation_UsesPositiveFailClosedLimitsWhenCurrentFramesCannotFit()
        {
            var coordinator = new DecodedFrameBudgetCoordinator(
                totalPhysicalMemoryBytesOverride: 16L * GiB,
                availablePhysicalMemoryBytesOverride: 64L * MiB);

            coordinator.UpdatePaneState(
                "pane-primary",
                isOpen: true,
                gpuActive: false,
                actualDecodeBackend: DecodeBackendNames.Cpu,
                approximateFrameBytes: 256 * (int)MiB,
                operationalQueueDepth: 1,
                sessionBudgetOverrideMegabytes: null);
            var secondary = coordinator.UpdatePaneState(
                "pane-secondary",
                isOpen: true,
                gpuActive: true,
                actualDecodeBackend: DecodeBackendNames.Vulkan,
                approximateFrameBytes: 4,
                operationalQueueDepth: 2,
                sessionBudgetOverrideMegabytes: null);
            var primary = coordinator.UpdatePaneState(
                "pane-primary",
                isOpen: true,
                gpuActive: false,
                actualDecodeBackend: DecodeBackendNames.Cpu,
                approximateFrameBytes: 256 * (int)MiB,
                operationalQueueDepth: 1,
                sessionBudgetOverrideMegabytes: null);

            Assert.Equal(1, primary.QueueDepth);
            Assert.Equal(2, secondary.QueueDepth);
            Assert.True(primary.PaneBudgetBytes > 0L);
            Assert.True(secondary.PaneBudgetBytes > 0L);
            Assert.True(primary.PaneBudgetBytes < 256L * MiB);
            Assert.True(secondary.PaneBudgetBytes < 4L);
            Assert.True(primary.PaneBudgetBytes + secondary.PaneBudgetBytes <= primary.SessionBudgetBytes);
        }

        [Fact]
        public void ResourceClassesAndAllocationSnapshots_NormalizeInputs()
        {
            var business = new DecodedFrameBudgetCoordinator(16L * GiB, 12L * GiB);
            var workstation = new DecodedFrameBudgetCoordinator(32L * GiB, 12L * GiB);
            var largeWorkstation = new DecodedFrameBudgetCoordinator(128L * GiB, 12L * GiB);

            Assert.Equal(HostResourceClass.Business16, business.HostResourceClass);
            Assert.Equal(HostResourceClass.Workstation32To64, workstation.HostResourceClass);
            Assert.Equal(HostResourceClass.Workstation128Plus, largeWorkstation.HostResourceClass);

            var businessAllocation = business.UpdatePaneState(
                null,
                isOpen: true,
                gpuActive: false,
                actualDecodeBackend: null,
                approximateFrameBytes: 0,
                operationalQueueDepth: 0,
                sessionBudgetOverrideMegabytes: null);
            var workstationAllocation = workstation.UpdatePaneState(
                "pane-primary",
                isOpen: true,
                gpuActive: false,
                actualDecodeBackend: null,
                approximateFrameBytes: 1,
                operationalQueueDepth: 0,
                sessionBudgetOverrideMegabytes: null);
            var largeAllocation = largeWorkstation.UpdatePaneState(
                "pane-primary",
                isOpen: true,
                gpuActive: false,
                actualDecodeBackend: null,
                approximateFrameBytes: 1,
                operationalQueueDepth: 0,
                sessionBudgetOverrideMegabytes: null);

            Assert.Equal("pane-primary", businessAllocation.PaneId);
            Assert.Equal(512L * MiB, businessAllocation.SessionBudgetBytes);
            Assert.Equal(1024L * MiB, workstationAllocation.SessionBudgetBytes);
            Assert.Equal(1536L * MiB, largeAllocation.SessionBudgetBytes);
            Assert.Equal(3, workstationAllocation.ForwardFrameTarget);
            Assert.Equal(2, largeAllocation.ForwardFrameTarget);

            var normalized = new PaneBudgetAllocation(
                null,
                DecodedFrameBudgetBand.SinglePaneCpu,
                HostResourceClass.Business16,
                -1L,
                -2L,
                -3,
                -4,
                -5,
                null,
                isGpuActive: false);
            Assert.Equal(string.Empty, normalized.PaneId);
            Assert.Equal(0L, normalized.SessionBudgetBytes);
            Assert.Equal(0L, normalized.PaneBudgetBytes);
            Assert.Equal(0, normalized.PreviousFrameTarget);
            Assert.Equal(0, normalized.ForwardFrameTarget);
            Assert.Equal(0, normalized.QueueDepth);
            Assert.Equal(string.Empty, normalized.ActualDecodeBackend);
            Assert.False(normalized.IsEquivalentTo(null));
            Assert.True(normalized.IsEquivalentTo(new PaneBudgetAllocation(
                string.Empty,
                DecodedFrameBudgetBand.SinglePaneCpu,
                HostResourceClass.Business16,
                0L,
                0L,
                0,
                0,
                0,
                string.Empty,
                isGpuActive: false)));
            Assert.Throws<ArgumentNullException>(() => new PaneBudgetAllocationChangedEventArgs(null));
        }
    }
}
