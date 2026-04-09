# Platform Core And Cross-Platform Plan

This note is a planning checkpoint. The current WPF app remains the working reference implementation. This pass adds only small neutral seams and does not start the UI migration.

## What Already Looks Core-Oriented

These responsibilities already map well to a future platform-neutral core:

- `Core\Abstractions\IVideoReviewEngine`: transport and seek/step contract
- `Core\Models\ReviewPosition`: review cursor truth
- `Core\Models\FrameDescriptor`: decoded frame identity and timing metadata
- `Core\Models\FrameStepResult`: exact step outcome
- `Core\Models\VideoMediaInfo`: media, stream, and audio capability metadata
- `Engines\FFmpeg\FfmpegReviewEngine`: decode, seek, index, cache, and playback lifecycle behavior

These are the main candidates to move behind a dedicated core library over time:

- media/timeline state
- playback state
- review cursor state
- file-global index readiness
- decoded review-cache state
- synchronized seek/step concepts
- future multi-video workspace coordination

## What Is Still WPF-Specific

The current WPF shell still owns responsibilities that should not be the long-term application core:

- `App.xaml.cs` runtime/bootstrap startup flow
- `MainWindow.xaml` and window layout
- `MainWindow.xaml.cs` control event handling, keyboard routing, slider drag state, focus management, and window/full-screen behavior
- `OpenFileDialog`, menus, tooltips, and text-box interaction rules
- `DispatcherTimer` usage for UI refresh and hold-to-repeat stepping
- `BitmapSource` presentation and `Image.Source` updates

There is also one important platform leak inside `Core`: `DecodedVideoFrame` currently carries `BitmapSource` and `PixelFormat`, which ties the current frame presentation contract to WPF.

## New Seams Added In This Pass

The scaffolding added here is intentionally small:

- `Core\Models\DecodedFrameBuffer`
  - a raw decoded-frame payload without WPF rendering types
  - intended to become the handoff object between the engine/core and future UI shells
- `Core\Models\ReviewPlaybackState`
  - a neutral playback-state enum instead of ad hoc UI booleans
- `Core\Models\ReviewSessionSnapshot`
  - a single-session state snapshot for future shell/view-model layers
- `Core\Coordination\ReviewPaneState`
  - a per-pane state object for synchronized multi-video review
- `Core\Coordination\MultiVideoWorkspaceState`
  - a top-level workspace snapshot for future multi-pane coordination
- `Core\Coordination\TimelineSynchronizationMode`
  - independent vs shared-timeline modes
- `Core\Coordination\SynchronizedOperationScope`
  - focused-pane vs all-pane operations

These types are scaffolding only. They do not yet change the current WPF app flow.

## Current Extraction Status

The current WPF shell now has a first real orchestration seam:

- `Core\Coordination\ReviewSessionCoordinator`
  - subscribes to engine state changes
  - maintains a neutral `ReviewSessionSnapshot`
- `Core\Coordination\ReviewWorkspaceCoordinator`
  - composes `ReviewSessionCoordinator`
  - owns the current single-pane `MultiVideoWorkspaceState`
  - routes current workspace-level commands such as open, close, play, pause, seek, and step
  - owns the bounded open/prepare/ready preparation-state flow that the current shell renders as cache/index status
  - now builds workspace state from stable pane bindings with explicit primary, active, and focused pane identity rather than one implicit pane snapshot
  - can now host more than one pane/session binding internally while still routing the current shell through the active primary pane
  - now supports bounded internal active/focused pane switching so command routing can move between internal pane bindings without changing the visible WPF shell yet
  - now exposes bounded shell-neutral pane inspection and selection methods for diagnostics and future shell orchestration
  - now supports bounded workspace operation scope for focused-pane vs all-pane transport routing, with all-pane commands currently routed sequentially and focused-pane-first for diagnostics and future shell orchestration
  - now exposes per-pane operation results for scoped workspace commands so diagnostics and future shells can inspect each targeted pane outcome explicitly without changing the current WPF host
  - those per-pane results now use a normalized contract with explicit targeted/attempted/outcome semantics and optional step payload only where it is actually meaningful
- `MainWindow.xaml.cs`
  - still owns WPF control behavior and rendering concerns
  - now consumes workspace/session coordinator state instead of reading raw engine state-change payloads or routing transport commands directly
  - renders preparation/cache status text from workspace state instead of sequencing open/prep transitions itself

## Multi-Video Model Sketch

The intended future model is a synchronized workspace rather than a pile of independent player windows.

- A workspace owns a master timeline position.
- Each pane owns one media session and exposes a `ReviewSessionSnapshot`.
- Pane identity should remain stable even when the session contents change, so pane ids and session ids should not be treated as the same thing.
- Each pane may also carry a stable label and an optional timeline offset relative to the master timeline.
- The workspace coordinator should be able to host multiple pane bindings before the shell exposes multi-pane UI, so multi-video composition can be proven independently of the WPF layout.
- Active pane selection should drive transport command routing, while focused pane selection stays available for future keyboard-oriented review interaction.
- Shells and diagnostics should be able to query bound panes and request pane selection through the workspace layer rather than reaching into internal coordinator bindings directly.
- Operations such as play, pause, seek, step forward, and step backward should be scoped explicitly:
  - focused pane only
  - all panes
- The current all-pane path should be treated as honest orchestration, not polished synchronization:
  - sequential routing is acceptable at first
  - focused-pane-first routing is useful so keyboard-oriented review still has a clear primary result
- The focused pane remains important for direct keyboard/frame-entry interaction.
- Absolute frame identity remains per session. A synchronized workspace should not pretend that multiple files share one literal frame number unless they actually do.
- Index/cache readiness also remains per session. A multi-video UI must surface when one pane is ready and another is still warming or indexing.

## Why Vulkan Is Not First

Vulkan is not the next architectural step because the current blocker is not graphics API choice.

- The current code still mixes review/session state and WPF shell behavior in `MainWindow.xaml.cs`.
- The current decoded-frame handoff is WPF-specific.
- Multi-video coordination needs a neutral state model before any GPU-specific presentation work is worth the risk.

Changing rendering APIs before those seams exist would increase complexity without solving the actual migration problem.

## Why Avalonia Is The Likely Next UI Path

Avalonia is the likely next shell because it offers:

- a desktop UI model close enough to the current WPF shell to keep migration risk practical
- Windows, macOS, and Linux reach without a full engine rewrite
- a path to keep the current WPF app as a reference host while a second shell is brought up incrementally

The key prerequisite is to move session/workspace state and decoded-frame contracts out of WPF-specific types first.

## Proposed Phases

1. Stabilize neutral state contracts
   - keep adding small core snapshots and coordination types
   - stop new WPF-only state from spreading through `MainWindow.xaml.cs`
2. Extract a session/workspace coordinator
   - move app/session orchestration out of `MainWindow.xaml.cs`
   - keep WPF as a thin host over that coordinator
3. Strengthen workspace composition
   - support stable pane identity separate from session identity
   - support explicit active/focused/primary pane state before visible multi-pane UI exists
4. Normalize frame presentation contracts
   - use a neutral raw-frame handoff at the core boundary
   - keep WPF-specific bitmap conversion in the WPF shell or an adapter layer
5. Introduce multi-video coordination
   - multiple panes
   - shared timeline operations
   - per-pane offsets, labels, readiness, and focus
   - active-pane command routing that can later expand beyond the current single-pane shell
6. Bring up an Avalonia shell
   - start with the same core/session coordinator
   - keep the WPF app alive as the reference host until parity is acceptable
7. Revisit rendering acceleration later
   - only after the shell and coordination seams are stable

## What This Pass Intentionally Did Not Touch

- no engine rewrite
- no Avalonia packages
- no Vulkan or GPU pipeline work
- no runtime-manifest/runtime-loading changes
- no user-facing behavior changes
- no move of major classes out of the existing WPF project

## Immediate Next Pass

The next practical pass should stay narrow:

1. Decide which scoped operations should eventually expose richer per-pane metadata such as explicit error details vs simple final-session snapshots.
2. Keep focused-pane routing as the visible shell default while active/focused/primary semantics stay distinct in the workspace layer.
3. Keep frame presentation exact and unchanged while the WPF shell becomes a thinner host.
