# D7 LIVE

D7 LIVE is a Windows livestream studio specification and implementation foundation for the `d7kt` brand. The target architecture is Tauri 2 + React + TypeScript for the shell, Rust for control/core logic, libobs/FFmpeg for mature media primitives, D3D11/Windows Graphics Capture for capture/composition, NVENC for encoding, and isolated provider/driver boundaries for TikTok events and virtual I/O.

## Current repository status

This repository contains the production architecture, UI shell, event model, alert queue, ticker model, profile/config models, update verification primitives, test-event flow, CI skeleton, release manifest format, and Windows build/release documentation.

The media and Windows driver layers require a Windows SDK/libobs build environment and, for distributable virtual devices, Windows driver signing. They are intentionally isolated instead of being faked.

## Quick start (Windows development machine)

1. Install Node.js 22 LTS, Rust stable MSVC, Visual Studio Build Tools 2022 with Desktop C++, WebView2 Runtime, CMake and Git.
2. `npm install`
3. `npm run build`
4. `cargo test --manifest-path src-tauri/Cargo.toml`
5. `npm run tauri dev` after adding the Tauri CLI to the project.

Read `docs/ARCHITECTURE.md` and `docs/BUILD_WINDOWS.md` before implementing media/driver integrations.
