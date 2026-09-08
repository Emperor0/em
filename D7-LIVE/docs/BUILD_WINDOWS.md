# Windows Build

## Prerequisites

- Windows 10/11 x64
- Visual Studio Build Tools 2022: Desktop development with C++
- Windows 10/11 SDK
- Node.js 22 LTS
- Rust stable `x86_64-pc-windows-msvc`
- WebView2 Runtime
- CMake + Ninja recommended
- Git

## UI/core

```powershell
npm install
npm run build
cargo test --manifest-path src-tauri/Cargo.toml
```

Install the Tauri CLI and run:

```powershell
npm run tauri dev
```

## Production media layer

Build libobs for Windows or consume a pinned, verified binary build. Do not dynamically download unsigned DLLs at runtime. Pin versions and verify hashes in CI.

## Virtual devices

Treat virtual camera and virtual audio as separately testable components. Production distribution must comply with Windows driver/code-signing requirements applicable to the chosen implementation.
