# JackBridge Handoff Notes

This repository is a custom fork of ProxyBridge, renamed JackBridge.

## Origin And Credit

- Upstream base: `https://github.com/InterceptSuite/ProxyBridge.git`
- Product name in this fork: JackBridge
- README should continue to credit ProxyBridge clearly.
- About box text requested by user: `Based on: ProxyBridge, improved by Jack Wang`

## Released Baselines

**JackBridge v1.5** — user's daily driver.

- Pushed to GitHub: `https://github.com/flazedude/JackBridge.git`
- Branch/main and tag `v1.5`
- Daily driver install path: `C:\Users\flaze\Desktop\JackBridge v1.5\JackBridge.exe`
- v1.5 is often running while beta builds are being tested. Do not kill it or launch/stop apps unless the user explicitly asks.

**JackBridge v3.0 Beta** — current branch `3.0-beta`.

- Apple-inspired dark redesign with sidebar navigation and dashboard
- System tray with proxy status indicator, click to restore
- Sidebar: Home, Connections, Activity, Rules, Settings, About
- Auto-scroll for activity and connection logs
- Geo asset presence check at startup
- Comprehensive README with full feature documentation
- 1100x650 default, 900x600 minimum window

## Current Branch: 3.0-beta

Recent commits:

```
61e0725 v3.6 — external proxy CPU fix, TCP direct cache, build system repair
26deba1 v3.5 — fix mihomo log level sync, reduce CPU via fewer packet threads and larger WinDivert queue
369582b v3.5 — dashboard logging controls, UDP error rate-limit, default mihomo log level to error
8d9f925 JackBridge v3.5 — performance optimizations, TCP-only bypass, health timer resilience, rebranding
61d7520 JackBridge v3.0 — comprehensive README with full feature documentation
1be34e4 v3.0 Beta — tray click, geo asset check, auto-scroll, cleanup
ad29e4c JackBridge v3.0 Beta — Apple-style redesign
1e1620e Redesign UI with sidebar navigation and dashboard
b329a8b JackBridge v3.0 beta — UI refresh foundation
0ff9a1d Add JackBridge v2.0 beta built-in proxy
361cfef Release JackBridge v1.5
```

## v3.0 Architecture

### UI Framework

- **Avalonia 11.3** with acrylic blur, system-native fonts
- Single accent color: Action Blue `#0066cc`
- Dark theme base: `#FF1d1d1f` content, `#FF000000` nav bar
- Window: 1100x650 default, 900x600 minimum
- Global nav bar (44px) with logo, nav links, proxy toggle, preferences gear menu
- Content area uses `ContentControl` with `DataTemplate` switching (no sidebar — nav links swap the full content)

### Navigation

Global nav links at top: Home, Connections, Rules, Settings, About.
Each sets `ActiveView` on `MainWindowViewModel`, which is data-templated to the corresponding view:

| Nav | ViewModel | View |
|-----|-----------|------|
| Home | `HomeViewModel` | `HomeView` |
| Connections | `ConnectionsViewModel` | `ConnectionsView` |
| Activity | `ActivityViewModel` | `ActivityView` |
| Rules | `ProxyRulesViewModel` | `ProxyRulesWindow` |
| Settings | `ProxySettingsViewModel` | `ProxySettingsWindow` |
| About | `AboutViewModel` | `AboutWindow` |

### Preferences Gear Menu (top-right nav)

- Traffic Logging toggle
- DNS via Proxy toggle
- Localhost via Proxy toggle
- Close to Tray toggle
- Run at Startup toggle
- Language: English / 中文

### System Tray

- Always available when app is running
- Shows proxy status (On/Off)
- Click to restore window from tray
- Close-to-tray behavior configurable via preferences

### Dashboard (HomeView)

- Proxy status dot (green/red) + text
- Engine type (External / Built-in)
- Active rules count
- Recent activity and connections previews
- Quick-access toggle button

## v3.0 Source Files

### Key C# Files

```
Windows/gui/
  App.axaml, App.axaml.cs          — Application entry, tray icon, lifecycle
  Program.cs                       — Startup, assembly resolve
  Common/
    RelayCommand.cs                — ICommand implementation
    ValidationHelper.cs            — IP/port/domain validation
  Interop/
    JackBridgeNative.cs            — P/Invoke wrappers for JackBridgeCore.dll
  Services/
    ConfigManager.cs               — Portable config.json read/write, AppConfig model
    JackBridgeService.cs           — Managed wrapper around native proxy engine
    MihomoService.cs               — Mihomo core management (install/start/stop/profile/DNS/GEO)
    AppLogger.cs                   — File-based session logger (logs/jackbridge-*.log)
    SettingsService.cs             — Windows startup registry, settings persistence
    UpdateService.cs               — GitHub release check, download, install
    WindowsProcessJob.cs           — Win32 job object for child process lifetime
    Loc.cs                         — Localization strings (en/zh)
  ViewModels/
    MainWindowViewModel.cs         — Central VM: proxy toggle, nav, rules, config, logging
    HomeViewModel.cs               — Dashboard state
    ConnectionsViewModel.cs        — Observed process list, filtering
    ActivityViewModel.cs           — Activity log display
    ProxySettingsViewModel.cs      — External/Built-in proxy config UI
    ProxyRulesViewModel.cs         — Rule CRUD, priority, import/export
    AboutViewModel.cs              — About box
    UpdateCheckViewModel.cs        — Update check dialog
    UpdateNotificationViewModel.cs — Update notification banner
    ViewModelBase.cs               — INotifyPropertyChanged base
  Views/
    MainWindow.axaml               — Global nav, content host, styles
    HomeView.axaml                 — Dashboard layout
    ConnectionsView.axaml          — Connections log + observed processes
    ActivityView.axaml             — Activity log
    ProxySettingsWindow.axaml      — Built-in/external proxy settings form
    ProxyRulesWindow.axaml         — Rules list, priority, add/edit/delete
    AddRuleDialog.axaml            — Add rule modal
    AboutWindow.axaml              — About box
    UpdateCheckWindow.axaml        — Update check dialog
    UpdateNotificationWindow.axaml — Update notification
```

### Native C Engine

```
Windows/src/
  JackBridge.c                     — WinDivert transparent proxy engine
  JackBridge.h                     — Native API header
```

Key native capabilities:
- WinDivert packet capture/redirect (TCP + UDP)
- Dynamic relay port selection (scans from 34010 upward)
- Process-based bypass at native level for mihomo.exe, jackbridge-mihomo.exe, etc.
- WinDivert open timeout (10s) to prevent hangs
- Other-JackBridge-instance relay port range bypass (34010–34209)

### CLI

```
Windows/cli/
  Program.cs                       — JackBridge_CLI.exe for scripting
```

## v3.0 Key Behaviors

### Proxy Toggle Flow (Start)

1. **STEP 1/3:** Check for other JackBridge instances (refuses if v1.5 running from different directory)
2. **STEP 2/3:** Start built-in mihomo core (if BuiltIn engine, with 2s stabilization delay after port opens)
3. **STEP 3/3:** Start native transparent proxy engine (WinDivert, 10s timeout)

If WinDivert fails in built-in mode, it's non-fatal — proxy runs with mihomo only and logs instructions to set system proxy.

### Portability

- `config.json` stored beside `JackBridge.exe`
- Native DLLs in `native/` subfolder (resolved at runtime)
- Mihomo core in `core/`, profiles in `profiles/`, GEO data in `data/`
- Logs in `logs/` (one file per session)

### Health Monitoring

- `System.Threading.Timer` (background thread) checks mihomo liveness every 3s
- Uses sync socket connect (no async deadlock risk)
- Auto-disables proxy if mihomo core dies

### Instance Guard

- `IsAnotherJackBridgeInstanceRunning()` checks for JackBridge.exe processes from different directories
- Prevents two transparent redirectors from fighting over WinDivert

### File Logging

- `AppLogger` writes to `logs/jackbridge-YYYY-MM-DD_HH-mm-ss.log`
- All activity log messages and native log callbacks included
- New file per run

## v1.5 → v3.0 Delta Summary

| Area | v1.5 | v3.0 |
|------|------|------|
| UI Framework | Avalonia (basic) | Avalonia 11.3 with acrylic blur, dark theme |
| Navigation | Menu buttons + popup windows | Global nav bar, content switching |
| Dashboard | None | HomeView with status, stats, recent activity |
| System Tray | Basic | Status indicator, click to restore |
| Connections View | Basic list | Deduplicated, searchable, auto-scroll |
| Activity View | Basic log | Searchable, auto-scroll |
| Built-in Proxy | None (v2 beta) | Full mihomo integration |
| Instance Guard | None | Multi-instance detection and refusal |
| Health Timer | DispatcherTimer (UI thread) | Threading.Timer (background, no deadlock) |
| Logging | UI-only | File-based AppLogger |
| Config | JSON beside exe | Same, with legacy migration |
| Styling | Default | Apple-inspired dark design system |

## Build Commands

### GUI

```powershell
dotnet build Windows\gui\JackBridge.GUI.csproj --no-restore
```

### CLI

```powershell
dotnet build Windows\cli\JackBridge.CLI.csproj --no-restore
```

### Native DLL (GCC/MinGW-w64)

```powershell
gcc -shared -O2 -flto -s -Wall -D_WIN32_WINNT=0x0601 -DJACKBRIDGE_EXPORTS -I"Windows\vendor\WinDivert-2.2.2-A\include" Windows\src\JackBridge.c -L"Windows\vendor\WinDivert-2.2.2-A\x64" -lWinDivert -lws2_32 -liphlpapi -o Windows\gui\bin\Debug\net10.0-windows\JackBridgeCore.dll
```

Expected GCC warnings (not blockers):
- Ignored MSVC `#pragma comment`
- Unused local `src_port` in packet processor
- Unused local `from_ip` in UDP relay server

### Quick Build Script

```powershell
cd Windows
.\compile.ps1 -NoSign
```

## Debugging Commands

Check relevant processes:

```powershell
Get-Process | Where-Object { $_.ProcessName -match 'mihomo|verge|JackBridge|Clash|WinDivert|tun|proxy' } | Select-Object Id,ProcessName,Path,Responding,CPU,StartTime | Sort-Object ProcessName
```

Check local proxy/relay ports:

```powershell
netstat -ano | Select-String ':34010|:34011|:34012|:34013|:8888|:9090|:7890|:7891|:7892|:7897|:53'
```

## Important User Preferences

- Do not launch the GUI for the user unless explicitly asked. Tell them the path instead.
- User does not want beta builds to affect v1.5 daily driver.
- User wants portable layout.
- User wants clean UI — no awkward grey space, no extra settings windows when avoidable.
- v1.5 is often running while beta builds are tested. Do not kill it.

## v3.5 — Performance Optimization (2026-05-05)

Branch: `3.0-beta`. All changes in `Windows/src/JackBridge.c`.

### Applied Optimizations

| # | Optimization | Detail |
|---|-------------|--------|
| 1 | Cached proxy IP | `g_proxy_host_ip` resolved once in `SetProxyConfig`, reused per `connection_handler`. Eliminates `getaddrinfo()` DNS call per TCP connection. |
| 2 | Per-bucket SRWLOCK | 256 `connection_locks` + 1024 `pid_cache_locks` + 1 `logged_lock` replace single global mutex. Packet threads on different hash buckets no longer contend. |
| 3 | UDP flow action cache | 512-entry hash table caches DIRECT/BLOCK decisions per `(src_ip, src_port)` for 30s. Repeat UDP packets from DIRECT flows skip the entire rule engine (PID lookup, process name, rule matching). Cleared on rule changes via `update_has_active_rules()`. |
| 4 | Process name cache | 256-entry hash table caches PID → process name for 30s. Avoids `OpenProcess` + `QueryFullProcessImageNameA` kernel round-trips for repeat PIDs. |
| 5 | Longer UDP PID TTL | `PID_CACHE_TTL_UDP_MS` = 30000ms (vs 1000ms for TCP). Long-lived UDP flows re-scan the system table 30x less often. |

### Reverted / Not Kept

- **Dual-thread relay** — spawning 2 threads per connection was resource-heavy and caused instability. The original inline `select()`-based `transfer_handler` is fine.
- **Pre-allocated PID table buffers** — caused `STATUS_HEAP_CORRUPTION` crash. A shared buffer pointer was copied outside the lock, then freed by another thread (use-after-free). Reverted to per-call `malloc`/`free`.

### Known Issues Carry-Over

- UpdateService has `GitHubApiUrl = null` — update checks are disabled
- Single-file publish with NativeAOT fails — no MSVC linker installed

## v3.6 — External Proxy CPU Fix & Build Repair (2026-05-09)

Branch: `3.0-beta`. All native changes in `Windows/src/JackBridge.c`.

### Root Cause: Dual CPU Spike

When an external proxy (clash-verge, mihomo) was running, JackBridge AND the external proxy spiked CPU together. Closing JackBridge immediately returned both to normal.

**Why:** Every data packet from a bypassed proxy process re-ran the full rule engine path:
1. `check_process_rule()` → PID lookup via `get_pid_for_port()`
2. Kernel table scan (`GetExtendedTcpTable` / `GetExtendedUdpTable`)
3. Process name resolution (`OpenProcess` + `QueryFullProcessImageNameA`)
4. Bypass name matching
5. Rule action decision

This happened for **every single data packet** of every bypassed TCP connection. With an external proxy forwarding all traffic to a remote server (generating hundreds of data packets per connection), this CPU cost was enormous — and since JackBridge was intercepting those packets, it directly affected the external proxy's ability to send/receive.

### Fix: TCP Direct Connection Cache

| # | Fix | Detail |
|---|-----|--------|
| 1 | TCP direct cache | 512-entry hash table caches `(src_ip, src_port)` → DIRECT for 60s. On first match of a bypassed proxy process, the connection is cached. All subsequent data packets skip the entire rule engine — just `WinDivertSend()` and continue. |
| 2 | UDP bypass (was TCP-only) | `check_process_rule()` no longer requires `!is_udp` for the bypass check. UDP from proxy processes now also goes DIRECT. |
| 3 | Expanded bypass list | Added `verge-mihomo-core`, `clash-meta.exe`, `mihomo-core.exe`, `clash-nyanpasu.exe` |
| 4 | PID cache TTL 5s | `PID_CACHE_TTL_MS`: 1000ms → 5000ms. Fewer kernel table scans per connection. |
| 5 | WinDivert queue 32ms | `WINDIVERT_PARAM_QUEUE_TIME`: 16ms → 32ms. More packet batching, fewer kernel transitions. |
| 6 | Transfer timeout 1s | `select()` timeout: 50ms → 1s. `select()` returns immediately on data; timeout only matters for truly idle connections. Reduces busy-polling CPU waste. |
| 7 | Linux parity | `NUM_PACKET_THREADS`: 4 → 2, `PID_CACHE_TTL_MS`: 1000ms → 5000ms |

### Build System

- **PublishAot disabled** (`false`) — this machine lacks Visual Studio/MSVC linker (`link.exe`). Native AOT requires it.
- **PublishTrimmed disabled** (`false`) — ILLink trimming strips Avalonia's `ReflectionBindingExtension` types, silently breaking UI bindings (activity box, connections box don't render).
- Both GUI and CLI csproj files set these to `false` for local builds. CI with VS installed should restore original settings.
- GCC used via MSYS2 MinGW-w64 at `C:\msys64\mingw64\bin\gcc.exe`.

### Desktop Package

Built to `C:\Users\flaze\Desktop\JackBridge v3.6\`:
- `JackBridge.exe` (GUI), `JackBridge_CLI.exe` (CLI)
- `JackBridgeCore.dll` (native redirect engine)
- `WinDivert.dll`, `WinDivert64.sys`, `WinDivert32.sys`
- All .NET runtime + Avalonia DLLs (self-contained, no framework dependency)
- `JackBridge.deps.json`, `JackBridge.runtimeconfig.json` (essential for .NET host launch)
