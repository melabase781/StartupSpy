# 🔎 StartupSpy

> A modern Windows startup manager built with C# and WPF (.NET 8)

![Platform](https://img.shields.io/badge/platform-Windows-blue)
![Framework](https://img.shields.io/badge/.NET-8.0-purple)
![Language](https://img.shields.io/badge/language-C%23-green)
![License](https://img.shields.io/badge/license-MIT-lightgrey)

StartupSpy is a sleek, dark-themed Windows desktop application that scans and displays everything that launches at startup — registry entries, startup folders, scheduled tasks, and services — with colour-coded risk assessment.

---

## 📸 Features

- **Full startup scan** across all 4 sources:
  - `HKLM` and `HKCU` Registry Run keys
  - User and Common Startup folders
  - Logon-triggered Scheduled Tasks
  - Auto-start Windows Services
- **Risk assessment** — Safe / Low / Medium / High / Unknown based on publisher and file existence
- **Live filtering** by category and free-text search (name, publisher, path)
- **Detail panel** — full path, command, registry location, description, status
- **Quick actions** — open file location in Explorer, copy path to clipboard
- **High-risk quick filter** — one click to surface all elevated-risk entries
- **Modern dark UI** — built entirely in WPF with no third-party UI libraries

---

## 🚀 Getting Started

### Prerequisites

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8)
- Visual Studio 2022 (Community edition is free)

### Build & Run

```bash
git clone https://github.com/yourusername/StartupSpy.git
cd StartupSpy
dotnet build
dotnet run
```

> **Note:** Run as Administrator for full access to all registry hives, services, and scheduled tasks. The app manifest requests elevation automatically.

### Build a Release Executable

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The output will be a single `.exe` in `bin/Release/net8.0-windows/win-x64/publish/`.

---

## 🏗 Project Structure

```
StartupSpy/
├── Models/
│   └── StartupEntry.cs          # Data model with risk/category enums
├── ViewModels/
│   └── MainViewModel.cs         # MVVM ViewModel, scan logic, filtering
├── Services/
│   └── StartupScannerService.cs # All scanning logic (registry, tasks, services)
├── Converters/
│   └── Converters.cs            # WPF value converters (risk colours, icons, etc.)
├── MainWindow.xaml              # Full UI layout
├── MainWindow.xaml.cs           # Code-behind (minimal — just filter event)
├── App.xaml / App.xaml.cs
└── StartupSpy.csproj
```

---

## 🔬 Risk Assessment Logic

| Level   | Criteria                                              |
|---------|-------------------------------------------------------|
| Safe    | Publisher in known-safe list (Microsoft, Google, etc.)|
| Low     | Known publisher, not in safe list                     |
| Medium  | Name contains keywords like "updater", "agent", etc.  |
| High    | File does not exist on disk                           |
| Unknown | Publisher could not be determined                     |

---

## 🛠 Tech Stack

| Technology | Purpose |
|---|---|
| C# 12 | Language |
| .NET 8 | Runtime |
| WPF | UI framework |
| MVVM pattern | Architecture |
| TaskScheduler NuGet | Scheduled task enumeration |
| WMI / Win32 API | Service and registry access |

---

## 📋 Roadmap

- [ ] Enable / disable entries directly from the UI
- [ ] Export report to CSV or HTML
- [ ] Startup impact score (boot delay estimate)
- [ ] System tray with background monitoring
- [ ] Per-entry VirusTotal lookup

---

## 📄 License

MIT — feel free to use, modify, and distribute.
