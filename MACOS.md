# Local macOS build

This launcher manages GitHub releases. It does not recompile console ROMs itself.
The catalog includes static recompilations, decompilation ports, remakes, and emulators.

## Build and run

Install the .NET 9 SDK, initialize the Core submodule, then run:

```sh
git submodule update --init --recursive
bash scripts/build-macos.sh osx-arm64
open publish/GithubLauncher-Mac.app
```

Use `osx-x64` for an Intel Mac. The published app includes the .NET runtime.
Set `DOTNET_BIN` to an SDK executable path when it is not on PATH.

## Find games for this Mac

The bundled audit covers 129 project repositories. It currently records 57 published
Mac builds, 40 Windows candidates, and 32 unknown/no stable release results. This includes
emulators as well as game ports. These numbers are release metadata, not gameplay tests.
The catalog includes Dusklight (Twilight Princess), Wind Waker HD, and Spider-Man: Edge
of Time Recompiled. Supplementary fork entries are merged with the upstream catalog.

The filters are "Versão para Mac", "Testado no CrossOver", "Para testar no CrossOver",
and "Sem avaliação". Unknown means unassessed, not incompatible. Hover over a support
label for its source and audited release. Refresh updates stale release evidence using
GitHub's API, caching it for 24 hours and stopping when rate limited. Offline startup uses
the full bundled catalog. Mac releases do not need a manual compatibility allowlist.

Downloads prefer native macOS archives and ARM builds when available. This installer
supports ZIP, TAR.GZ, TAR.XZ, TGZ, and DMG. PKG and other system installers
require manual installation; locate the installed app afterward. Disk images are mounted
read-only and detached after copying. License prompts are never auto-accepted.
Intel-only Mac binaries may require Rosetta. Each game's original data/ROM revision,
graphics requirements, and first-run import procedure remain project-specific.
Read the project's setup guide before downloading. No ROMs are supplied.

## Windows builds through CrossOver

In Settings, configure the CrossOver app path and an existing bottle name.
Right-click a library game and choose "Configurar CrossOver…" to save a per-game bottle
and opt into testing unverified Windows builds. Adding a candidate opens this dialog.
Opt-in does not mark a game as tested. Create and
configure bottles in CrossOver first; choose graphics settings and install dependencies
according to the game's own instructions. A per-game `bottle` overrides the default.
The launcher executes CrossOver's `wine --bottle ... --workdir ... --cx-app ...` using
individual process arguments, without a shell or Proton.

No Windows games are pre-certified. After testing the exact Windows port in CrossOver,
add a record to `~/Library/Application Support/GithubLauncherMac/mac-compatibility.json` (user overrides), keyed by repository:

```json
{
  "owner/project": {
    "mode": "crossover",
    "release": "v1.0",
    "assetName": "Project-Windows.zip",
    "verified": true,
    "evidence": "Your test report: Mac model, macOS, CrossOver version, graphics backend, gameplay result",
    "requirements": "Required original game data and runtime dependencies",
    "bottle": "Recomp Games"
  }
}
```

User entries override the bundled entries by repository. A different Windows release or asset requires
new verification. Windows games whose renderer requires unsupported Vulkan features
may fail even when other Windows games work in the same bottle. ProtonDB ratings do not
establish CrossOver compatibility. The compiled console CPU code is only part of a port;
its renderer, audio, input, and runtime also need to work on the target platform.

## Validation and limitations

```sh
dotnet run --project tests/MacCompatibility.Tests.csproj
```

Launcher self-updates default to disabled on Mac to preserve this adaptation. Game updates
remain available. Native Mac release changes are filtered by platform filename; they are
not automatically gameplay-tested. Upstream compiler warnings remain.

Build artifacts are development bundles, without Developer ID signing or notarization.
Maintainer signing/notarization credentials have not been configured.
macOS settings, app lists, cache, and default installs now live under
`~/Library/Application Support/GithubLauncherMac/`. Existing portable configuration is
copied once without overwriting user files. `GITHUB_LAUNCHER_DATA_DIR` overrides the data
folder for isolated testing. Custom game install paths remain supported.

The build script bundles a matching Homebrew SDL2 library when available (override with
`SDL2_LIBRARY`). Without SDL2, the app keeps working with keyboard/mouse; controller
input is unavailable. Selecting an executable is saved by the existing launcher UI.
Recording gameplay results remains follow-up work.
