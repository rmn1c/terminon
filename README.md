# Terminon SSH Client

A modern, feature-rich Windows SSH client built with C# / .NET 8 / WPF.

![Dark theme terminal with tabs](docs/screenshots/placeholder-main.png)

---

## Features

### Connection Management
- Connect by password or private key (RSA, Ed25519)
- Save/load connection profiles to JSON (passwords encrypted with Windows DPAPI)
- Quick-connect bar: type `user@host[:port]` and hit Enter
- Recent connections dropdown
- Multiple simultaneous sessions in tabs
- Sidebar organising saved connections in folders

### Terminal Emulator
- Full VT100 / VT220 / xterm-256color emulation
  - SGR colours: 16-colour, 256-colour, and 24-bit RGB (true colour)
  - Cursor movement, erase, insert/delete, scrolling regions
  - Line-drawing characters (G0/G1 charset)
  - Alternate screen buffer (vim, htop, etc.)
- Scrollback buffer (default 10 000 lines, configurable)
- Copy (Ctrl+C when text selected), Paste (Ctrl+V / right-click)
- Configurable font family, size, and colour scheme (dark / light)
- Bell: visual flash or system sound
- Search within terminal output (Ctrl+F)
- GlyphRun-based WPF renderer for high performance

### SSH Features
- Interactive shell sessions
- SFTP/SCP file-transfer panel: browse, upload, download with progress
- Port forwarding (local and remote) configuration dialog
- SSH key generation (RSA 2048/4096 or Ed25519) built into the app
- Known-hosts management with fingerprint verification prompts

### UI/UX
- Modern WPF dark theme (Catppuccin Mocha) with light theme option
- System-tray support for background connections
- Keyboard shortcuts:
  - `Ctrl+T` — new tab
  - `Ctrl+W` — close tab
  - `Ctrl+Tab` / `Ctrl+Shift+Tab` — next / previous tab
  - `Ctrl+B` — toggle sidebar
  - `Ctrl+F` — search
  - `Shift+PageUp/Down` — scroll terminal buffer

---

## Project Structure

```
Terminon/
├── Terminon.sln
└── src/
    └── Terminon/
        ├── App.xaml / App.xaml.cs          — Application entry point, Serilog setup
        ├── Models/                          — Pure data models (ConnectionProfile, etc.)
        │   ├── ConnectionProfile.cs
        │   ├── ConnectionFolder.cs
        │   ├── AppSettings.cs
        │   ├── KnownHost.cs
        │   ├── PortForwardRule.cs
        │   └── SftpEntry.cs
        ├── Terminal/                        — VT100 parser and buffer (no WPF dependencies)
        │   ├── TerminalCell.cs
        │   ├── TerminalColor.cs
        │   └── TerminalBuffer.cs
        │   └── VT100Parser.cs
        ├── Services/                        — Business logic / I/O
        │   ├── SettingsService.cs
        │   ├── ProfileService.cs            — DPAPI password encryption
        │   ├── SshConnectionService.cs      — SSH.NET wrapper, host-key verification
        │   ├── SftpService.cs
        │   ├── KeyService.cs
        │   └── KnownHostsService.cs
        ├── Infrastructure/
        │   ├── RelayCommand.cs              — ICommand implementations (sync & async)
        │   ├── ViewModelBase.cs             — INotifyPropertyChanged base class
        │   └── ServiceLocator.cs
        ├── Converters/                      — WPF value converters
        ├── ViewModels/                      — MVVM view-models
        │   ├── MainViewModel.cs
        │   ├── SessionTabViewModel.cs
        │   ├── ConnectionDialogViewModel.cs
        │   ├── SftpPanelViewModel.cs
        │   ├── PortForwardingViewModel.cs
        │   ├── KeyGenerationViewModel.cs
        │   └── SettingsViewModel.cs
        ├── Controls/
        │   ├── TerminalControl.cs           — Custom DrawingContext terminal renderer
        │   └── SearchBar.xaml / .cs
        ├── Views/
        │   ├── MainWindow.xaml / .cs
        │   ├── ConnectionDialog.xaml / .cs
        │   ├── SftpPanel.xaml / .cs
        │   ├── PortForwardingDialog.xaml / .cs
        │   ├── KeyGenerationDialog.xaml / .cs
        │   ├── SettingsDialog.xaml / .cs
        │   ├── KnownHostDialog.xaml / .cs
        │   └── InputDialog.xaml.cs
        └── Resources/
            ├── Themes/
            │   ├── DarkTheme.xaml           — Catppuccin Mocha
            │   └── LightTheme.xaml
            └── Styles/
                └── CommonStyles.xaml
```

---

## Prerequisites

| Requirement | Version |
|---|---|
| Windows | 10 / 11 |
| .NET SDK | **8.0** |
| Visual Studio | 2022 17.8+ (or `dotnet` CLI) |

---

## Build & Run

### Option 1 — Visual Studio

1. Open `Terminon.sln`
2. Restore NuGet packages (automatic on first build)
3. Set `Terminon` as startup project
4. Press **F5** (debug) or **Ctrl+F5** (release)

### Option 2 — dotnet CLI

```powershell
# From the repo root
cd src\Terminon
dotnet restore
dotnet build -c Release
dotnet run -c Release
```

### Publish a self-contained exe

```powershell
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -o publish\
```

---

## NuGet Packages

| Package | Purpose |
|---|---|
| `SSH.NET 2024.0.0` | SSH client, SFTP, port forwarding |
| `Serilog 4.x` | Structured logging |
| `Serilog.Sinks.File` | Rolling log files |
| `Serilog.Sinks.Debug` | Debug output window |

---

## Configuration & Data Files

All application data is stored in `%APPDATA%\Terminon\`:

| File | Contents |
|---|---|
| `settings.json` | Application settings |
| `profiles.json` | Saved connection profiles (passwords DPAPI-encrypted) |
| `known_hosts.json` | Trusted SSH host fingerprints |
| `Logs\terminon-YYYY-MM-DD.log` | Rolling log files (7 days retained) |

---

## Security Notes

- **Passwords** are never stored in plaintext. They are encrypted with `ProtectedData` (DPAPI) scoped to the current Windows user account.
- **Known hosts** fingerprints are verified on every connection. Changed fingerprints trigger a warning dialog.
- **Private key files** have their permissions restricted to the current user on save.
- All SSH I/O is done on background threads; the UI thread is never blocked.

---

## Screenshots

> _Replace these placeholders with actual screenshots after first launch._

| Main window (dark) | SFTP panel | Connection dialog |
|---|---|---|
| ![main](docs/screenshots/placeholder-main.png) | ![sftp](docs/screenshots/placeholder-sftp.png) | ![conn](docs/screenshots/placeholder-conn.png) |

---

## Architecture

The project follows **Clean Architecture with MVVM**:

```
Views ──→ ViewModels ──→ Services ──→ Models
           (async)       (async)
                    ↑
              Infrastructure
         (RelayCommand, ViewModelBase)
```

- **No view code in ViewModels** — dialogs are triggered via `Func<Task<T>>` delegates.
- **No blocking calls** — all SSH/SFTP operations are `async`/`await` with `CancellationToken`.
- **Thread safety** — `TerminalBuffer` uses a lock; rendering snapshots are taken on the UI thread.

---

## Contributing

Pull requests welcome. Please:
1. Run `dotnet build` and fix any warnings before submitting
2. Follow existing naming conventions
3. Add/update XML docs on public APIs
