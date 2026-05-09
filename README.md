# JackBridge v3.6

Route Windows application traffic through HTTP or SOCKS5 proxies with a modern desktop UI. JackBridge uses native packet interception (WinDivert) and gives you fine-grained process-level control.

JackBridge is a custom build maintained by Jack Wang.

---

## Features

### Modern Desktop UI

- **Apple-inspired dark design** — clean interface with acrylic blur, system-native fonts, and a single accent color
- **Sidebar navigation** — Home, Connections, Activity, Rules, and Settings in one window
- **Dashboard** — at-a-glance proxy status, active rule count, engine type, and recent activity
- **System tray** — minimize to tray, restore with a click, proxy status always visible
- **Responsive layout** — 1100x650 default, 900x600 minimum

### Proxy Engine

- **External proxy mode** — route through any HTTP or SOCKS5 proxy
- **Built-in proxy engine** — optional Mihomo-based embedded proxy, no external server needed
- **One-click toggle** — graphical on/off switch from the dashboard or tray
- **Packet-level interception** — WinDivert kernel driver captures traffic before it hits the network

### Rules & Routing

- **Process-based rules** — target specific executables (`chrome.exe`, `Codex.exe`, etc.)
- **Rule priority model** — rules evaluated top-to-bottom; earlier rules win
- **Active vs Static rules** — active rules take precedence over static catch-alls
- **Quick-add from activity** — right-click any observed process and turn it into a rule
- **Wildcard rules** — `*` matches all processes for global proxy routing

### Activity & Connections

- **Live traffic view** — see every intercepted packet in real time
- **Auto-scroll** — automatically follows new activity as it arrives
- **Connection tracking** — monitor active connections, source/destination, and bytes transferred
- **Geo asset check** — geographic verification of connection endpoints

### Portability & Config

- **Portable config** — `config.json` stored beside `JackBridge.exe`, copy the folder to another machine
- **Multiple instances** — run beta and stable builds side-by-side without conflicts
- **Settings persistence** — proxy config, rules, and preferences saved across restarts

### Updates & Diagnostics

- **Update checking** — optional check for new releases with desktop notification
- **Activity logging** — detailed traffic logs for debugging
- **Error resilience** — handles driver load failures gracefully

---

## Installation

Download the latest release from the [Releases](https://github.com/flazedude/jackbridge-new/releases) page.

Extract the ZIP and run `JackBridge.exe`. On first launch you may be prompted to install the WinDivert driver — accept to enable traffic interception.

```
JackBridge\
  JackBridge.exe
  JackBridge_CLI.exe
  JackBridgeCore.dll
  WinDivert.dll
  WinDivert64.sys
  config.json          (created on first run)
```

---

## Usage

### Quick Start

1. Launch `JackBridge.exe`
2. Go to **Settings** and configure your proxy (HTTP or SOCKS5)
3. Go to **Rules** and add process rules (e.g., `chrome.exe` → Proxy)
4. Click the **proxy toggle** on the Home dashboard to enable

### Rule Examples

```
Active rules
  1. Codex.exe    → DIRECT    (bypass proxy)
  2. chrome.exe   → PROXY     (route through proxy)

Static rules
  1. *            → DIRECT    (everything else direct)
```

In this setup, `Codex.exe` stays direct (priority 1), `chrome.exe` goes through the proxy, and the wildcard `*` catches everything else direct.

### CLI

`JackBridge_CLI.exe` provides command-line access for scripting:

```
JackBridge_CLI --help
```

---

## Building

**Requirements:**

- Windows
- .NET 10 SDK
- GCC (MinGW-w64) for the native core
- WinDivert 2.2.2-A (place at `C:\WinDivert-2.2.2-A` or update `Windows\compile.ps1`)

**Quick build:**

```powershell
cd Windows
.\compile.ps1 -NoSign
```

**Manual build:**

```powershell
# Native core DLL
gcc -shared -O2 -DJACKBRIDGE_EXPORTS -I"C:\WinDivert-2.2.2-A\include" -L"C:\WinDivert-2.2.2-A\x64" `
    -o JackBridgeCore.dll src\JackBridge.c -lWinDivert -lws2_32 -liphlpapi

# GUI
dotnet publish gui\JackBridge.GUI.csproj -c Release -r win-x64 --self-contained `
    -p:PublishAot=false -p:PublishTrimmed=false -o publish\gui

# CLI
dotnet publish cli\JackBridge.CLI.csproj -c Release -r win-x64 --self-contained `
    -p:PublishAot=false -p:PublishTrimmed=false -o publish\cli
```

Copy `WinDivert.dll`, `WinDivert64.sys` from `C:\WinDivert-2.2.2-A\x64\` into the output folder alongside the built binaries.

---

## Tech Stack

| Layer | Technology |
|-------|-----------|
| UI Framework | Avalonia 11.3 |
| Language | C# (.NET 10) |
| Native Core | C (GCC/MinGW-w64) |
| Packet Interception | WinDivert 2.2 |
| Built-in Proxy | Mihomo (Clash Meta) |
| Installer | NSIS |

---

## License

This project is derived from ProxyBridge. See the repository license and retain upstream attribution when redistributing modified builds.

---

*Based on [ProxyBridge](https://github.com/InterceptSuite/ProxyBridge) by InterceptSuite.*
