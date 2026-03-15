# AOE — Audio Over Ethernet

Stream system audio from **Computer Beta** (Windows 11) to **Computer Alpha** (Windows 11) over the network. Both apps run in the system tray and show connection status.

## Requirements

- **Rust** — install first (see below)
- **Windows 11** (sender uses WASAPI loopback; receiver uses cpal; this setup targets Windows)

### Installing Rust (Windows)

1. Download and run **rustup-init.exe**: https://win.rustup.rs/x86_64  
2. Accept the default options (press Enter).  
3. When it finishes, **close and reopen PowerShell** (or restart the terminal) so `cargo` is on your PATH.  
4. For linking, Rust needs the Microsoft linker (`link.exe`). Either:
   - Install [Build Tools for Visual Studio](https://visualstudio.microsoft.com/visual-cpp-build-tools/) with **“Desktop development with C++”**, then run **Developer PowerShell for VS** (Start menu) and run `cargo build` from there; or
   - Run `.\find-linker.ps1` in this repo to find `link.exe` and add its folder to your PATH; or
   - Use the GNU toolchain instead (no Visual Studio): `rustup default stable-x86_64-pc-windows-gnu` and install MinGW via [MSYS2](https://www.msys2.org/) (`pacman -S mingw-w64-ucrt-x86_64-toolchain`), then add `C:\msys64\ucrt64\bin` to PATH.

## Build

From the repo root:

```bash
cargo build --release
```

Binaries:

- `target/release/aoe-sender.exe` — run on **Beta** (audio source)
- `target/release/aoe-receiver.exe` — run on **Alpha** (audio playback)

## Usage

### 1. Alpha (receiver)

Start first so it is listening:

```bash
aoe-receiver [PORT]
```

Default port: `38472`. Example:

```bash
aoe-receiver
# or
aoe-receiver 38472
```

Tray icon: **AOE Receiver**. Tooltip: *Waiting* → *Connected* when Beta connects.

### 2. Beta (sender)

Connect to Alpha’s IP and port:

```bash
aoe-sender <ALPHA_IP> [PORT]
```

Examples:

```bash
aoe-sender 192.168.1.100
aoe-sender 192.168.1.100 38472
```

Replace `192.168.1.100` with Alpha’s actual IP (or hostname). If you run both on one machine, use `127.0.0.1`.

Tray icon: **AOE Sender**. Tooltip: *Idle* → *Streaming* when connected and sending.

### 3. Quit

Right‑click the tray icon → **Quit** on either app.

## How it works

- **Sender (Beta):** Uses WASAPI to capture the default **render** (playback) device in loopback mode (system audio). Encodes 44.1 kHz stereo f32 PCM and sends it over TCP to Alpha.
- **Receiver (Alpha):** Listens for TCP connections, reads the stream, and plays it with **cpal** on the default output device. A small queue smooths network jitter.

## Firewall

On **Alpha**, allow inbound TCP on the chosen port (e.g. 38472) so Beta can connect.

## Notes

- **Loopback:** The sender uses the default Windows output device in loopback mode. If your setup doesn’t support that (e.g. some drivers), you may only get silence or need to use application-specific loopback (not implemented here).
- **Latency:** Expect roughly a few hundred ms due to buffering and network. Tuning chunk size and buffer in code can reduce it at the cost of stability.
