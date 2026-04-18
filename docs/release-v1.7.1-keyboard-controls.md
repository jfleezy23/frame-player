# Frame Player v1.7.1 Release Note

This note documents the `v1.7.1` release. It is the maintainer-facing summary of what is new on top of `v1.7.0`, what remains intentionally deferred, and what validation evidence backs this release.

## What Is New In v1.7.1

- Playback hotkeys now match the current MVP review feedback:
  - `,` rewinds 5 seconds
  - `.` fast-forwards 5 seconds
  - `L` toggles loop playback
- Existing review muscle memory is not fully broken:
  - `J` still rewinds 5 seconds as a compatibility alias
- Release-facing shortcut references were updated together:
  - the Playback menu now shows `,`, `.`, and `L`
  - the Help dialog now documents the same bindings
  - the repository README now points at the same shipped controls

## What Stays Deferred

- Zoom remains deferred to a future review pass.
- Render-side geometry and color investigation is not part of `v1.7.1`.
- No batch export, alternate container chooser, codec chooser, or comparison-render movie ships in `v1.7.1`.

## Runtime And CI Truth

- Product version: `v1.7.1`
- Current published clean-runner bootstrap asset: `v1.5.0`
- `Runtime\runtime-manifest.json` remains pinned to the verified runtime bundle because the playback DLL payload itself did not change for this patch release.

## Validation Evidence

Validation captured for this release:

- Local compile validation:
  - tool: .NET SDK `10.0.202`
  - command: `dotnet build FramePlayer.csproj -c Release -p:Platform=x64`
  - result: `Build succeeded`, `0 Warning(s)`, `0 Error(s)`
- Release verification validation:
  - command: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Build-TestDrop.ps1 -Configuration Release -Platform x64`
  - result: versioned release output plus validated `bin\TestDrop` verification output
- Automated tests:
  - no runnable test-project file was present in this checkout under `tests\FramePlayer.Core.Tests`, so no separate test suite was executed in this release pass

## Release Guidance

- Treat `v1.7.1` as a focused patch release on top of `v1.7.0` that aligns the shipped keyboard review controls with tester feedback.
- Keep `Properties\AssemblyInfo.cs` as the canonical product-version source.
- Release outputs for this cut should match the validated versioned application artifact and `bin\TestDrop` verification output.
