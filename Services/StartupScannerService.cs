using Microsoft.Win32;
using StartupSpy.Models;
using System.IO;
using System.ServiceProcess;

namespace StartupSpy.Services
{
    public class StartupScannerService
    {
        private static readonly HashSet<string> KnownSafePublishers = new(StringComparer.OrdinalIgnoreCase)
        {
            "Microsoft Corporation", "Microsoft", "Google LLC", "Apple Inc.",
            "Adobe Inc.", "Adobe Systems", "NVIDIA Corporation", "Intel Corporation",
            "AMD", "Realtek", "Logitech", "Razer", "Dell", "HP", "Lenovo"
        };

        private static readonly HashSet<string> HighRiskKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "updater", "update", "helper", "launcher", "agent", "daemon",
            "toolbar", "coupon", "deal", "offer", "search", "browser"
        };

        public bool ToggleEntry(StartupEntry entry)
        {
            try
            {
                switch (entry.Category)
                {
                    case StartupCategory.Registry:
                        return ToggleRegistryEntry(entry);
                    case StartupCategory.StartupFolder:
                        return ToggleStartupFolderEntry(entry);
                    case StartupCategory.ScheduledTask:
                        return ToggleScheduledTask(entry);
                    case StartupCategory.Service:
                        return ToggleService(entry);
                    default:
                        return false;
                }
            }
            catch { return false; }
        }

        private bool ToggleRegistryEntry(StartupEntry entry)
        {
            // Disabling: move value to a "disabled" backup key; enabling: move it back
            var isHklm = entry.Location.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase);
            var hive = isHklm ? Registry.LocalMachine : Registry.CurrentUser;
            var activeKey = entry.Location.Replace("HKLM\\", "").Replace("HKCU\\", "");
            var disabledKey = activeKey.Replace("\\Run", "\\Run-Disabled").Replace("\\RunOnce", "\\RunOnce-Disabled");

            if (entry.IsEnabled)
            {
                // Disable: copy to disabled key, delete from active key
                using var src = hive.OpenSubKey(activeKey, true);
                if (src == null) return false;
                var val = src.GetValue(entry.Name);
                if (val == null) return false;
                using var dst = hive.CreateSubKey(disabledKey, true);
                dst.SetValue(entry.Name, val);
                src.DeleteValue(entry.Name);
                return true;
            }
            else
            {
                // Enable: move back from disabled key to active key
                using var src = hive.OpenSubKey(disabledKey, true);
                if (src == null) return false;
                var val = src.GetValue(entry.Name);
                if (val == null) return false;
                using var dst = hive.CreateSubKey(activeKey, true);
                dst.SetValue(entry.Name, val);
                src.DeleteValue(entry.Name);
                return true;
            }
        }

        private bool ToggleStartupFolderEntry(StartupEntry entry)
        {
            var file = entry.Command;
            if (!File.Exists(file)) return false;
            var dir = Path.GetDirectoryName(file)!;

            // Strip all stacked .disabled suffixes to get the clean base name
            var baseName = Path.GetFileName(file);
            while (baseName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                baseName = baseName[..^9]; // remove trailing ".disabled"

            if (entry.IsEnabled)
            {
                // Disable: rename to baseName.disabled
                var dest = Path.Combine(dir, baseName + ".disabled");
                File.Move(file, dest);
            }
            else
            {
                // Enable: rename back to clean base name
                var dest = Path.Combine(dir, baseName);
                File.Move(file, dest);
            }
            return true;
        }

        private bool ToggleScheduledTask(StartupEntry entry)
        {
            using var ts = new Microsoft.Win32.TaskScheduler.TaskService();
            var task = ts.GetTask(entry.Location);
            if (task == null) return false;
            task.Enabled = !entry.IsEnabled;
            return true;
        }

        private bool ToggleService(StartupEntry entry)
        {
            // Change startup type between Automatic and Disabled via registry
            var svcName = entry.Name;
            // Find the service name (we stored DisplayName, need ServiceName)
            var services = System.ServiceProcess.ServiceController.GetServices();
            var svc = services.FirstOrDefault(s => s.DisplayName == svcName || s.ServiceName == svcName);
            if (svc == null) return false;

            var regPath = $@"SYSTEM\CurrentControlSet\Services\{svc.ServiceName}";
            using var key = Registry.LocalMachine.OpenSubKey(regPath, true);
            if (key == null) return false;

            // StartType: 2 = Automatic, 4 = Disabled
            key.SetValue("Start", entry.IsEnabled ? 4 : 2, RegistryValueKind.DWord);
            return true;
        }

        public List<StartupEntry> ScanAll()
        {
            var entries = new List<StartupEntry>();
            entries.AddRange(ScanRegistryKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false));
            entries.AddRange(ScanRegistryKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce", false));
            entries.AddRange(ScanRegistryKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true));
            entries.AddRange(ScanStartupFolders());
            entries.AddRange(ScanScheduledTasks());
            entries.AddRange(ScanServices());
            return entries;
        }

        private List<StartupEntry> ScanRegistryKey(string keyPath, bool localMachine)
        {
            var entries = new List<StartupEntry>();
            var hive = localMachine ? Registry.LocalMachine : Registry.CurrentUser;
            var hivePrefix = localMachine ? "HKLM" : "HKCU";

            // Scan active (enabled) entries
            try
            {
                using var key = hive.OpenSubKey(keyPath, false);
                if (key != null)
                {
                    foreach (var valueName in key.GetValueNames())
                    {
                        var command = key.GetValue(valueName)?.ToString() ?? "";
                        var filePath = ExtractFilePath(command);
                        entries.Add(new StartupEntry
                        {
                            Name = valueName,
                            Command = command,
                            FilePath = filePath,
                            Publisher = GetFilePublisher(filePath),
                            Category = StartupCategory.Registry,
                            Location = $"{hivePrefix}\\{keyPath}",
                            IsEnabled = true,
                            Risk = AssessRisk(valueName, filePath, command),
                            Description = GetFileDescription(filePath)
                        });
                    }
                }
            }
            catch { /* Access denied is common */ }

            // Scan disabled entries (moved to Run-Disabled / RunOnce-Disabled by ToggleRegistryEntry)
            var disabledKeyPath = keyPath.Replace("\\Run", "\\Run-Disabled").Replace("\\RunOnce", "\\RunOnce-Disabled");
            try
            {
                using var disabledKey = hive.OpenSubKey(disabledKeyPath, false);
                if (disabledKey != null)
                {
                    foreach (var valueName in disabledKey.GetValueNames())
                    {
                        var command = disabledKey.GetValue(valueName)?.ToString() ?? "";
                        var filePath = ExtractFilePath(command);
                        entries.Add(new StartupEntry
                        {
                            Name = valueName,
                            Command = command,
                            FilePath = filePath,
                            Publisher = GetFilePublisher(filePath),
                            Category = StartupCategory.Registry,
                            Location = $"{hivePrefix}\\{disabledKeyPath}",
                            IsEnabled = false,
                            Risk = AssessRisk(valueName, filePath, command),
                            Description = GetFileDescription(filePath)
                        });
                    }
                }
            }
            catch { }

            return entries;
        }

        private List<StartupEntry> ScanStartupFolders()
        {
            var entries = new List<StartupEntry>();
            var folders = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)
            };
            foreach (var folder in folders)
            {
                if (!Directory.Exists(folder)) continue;
                foreach (var file in Directory.GetFiles(folder))
                {
                    var isDisabled = file.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
                    // Strip all stacked .disabled suffixes to get the clean original filename
                    var cleanFile = file;
                    while (cleanFile.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                        cleanFile = cleanFile[..^9];
                    var filePath = ResolveShortcut(cleanFile);
                    entries.Add(new StartupEntry
                    {
                        Name = Path.GetFileNameWithoutExtension(cleanFile),
                        Command = file, // actual path on disk (may have .disabled suffix)
                        FilePath = filePath,
                        Publisher = GetFilePublisher(filePath),
                        Category = StartupCategory.StartupFolder,
                        Location = folder,
                        IsEnabled = !isDisabled,
                        Risk = AssessRisk(Path.GetFileName(cleanFile), filePath, cleanFile),
                        Description = GetFileDescription(filePath)
                    });
                }
            }
            return entries;
        }

        private List<StartupEntry> ScanScheduledTasks()
        {
            var entries = new List<StartupEntry>();
            try
            {
                using var ts = new Microsoft.Win32.TaskScheduler.TaskService();
                foreach (var task in ts.RootFolder.AllTasks)
                {
                    if (!task.Enabled) continue;
                    var hasLogonTrigger = task.Definition.Triggers
                        .Any(t => t.TriggerType == Microsoft.Win32.TaskScheduler.TaskTriggerType.Logon
                               || t.TriggerType == Microsoft.Win32.TaskScheduler.TaskTriggerType.Boot);
                    if (!hasLogonTrigger) continue;

                    var action = task.Definition.Actions.OfType<Microsoft.Win32.TaskScheduler.ExecAction>().FirstOrDefault();
                    var filePath = action?.Path ?? "";
                    entries.Add(new StartupEntry
                    {
                        Name = task.Name,
                        Command = $"{action?.Path} {action?.Arguments}".Trim(),
                        FilePath = filePath,
                        Publisher = GetFilePublisher(filePath),
                        Category = StartupCategory.ScheduledTask,
                        Location = task.Path,
                        IsEnabled = task.Enabled,
                        Risk = AssessRisk(task.Name, filePath, action?.Path ?? ""),
                        Description = task.Definition.RegistrationInfo.Description
                    });
                }
            }
            catch { }
            return entries;
        }

        private List<StartupEntry> ScanServices()
        {
            var entries = new List<StartupEntry>();
            try
            {
                var services = ServiceController.GetServices()
                    .Where(s => s.StartType == ServiceStartMode.Automatic);
                foreach (var svc in services)
                {
                    try
                    {
                        var regPath = $@"SYSTEM\CurrentControlSet\Services\{svc.ServiceName}";
                        using var key = Registry.LocalMachine.OpenSubKey(regPath);
                        var imagePath = key?.GetValue("ImagePath")?.ToString() ?? "";
                        var filePath = ExtractFilePath(imagePath);
                        var publisher = GetFilePublisher(filePath);
                        // Only show non-Microsoft services to keep the list manageable
                        if (KnownSafePublishers.Contains(publisher) && publisher.Contains("Microsoft"))
                            continue;
                        entries.Add(new StartupEntry
                        {
                            Name = svc.DisplayName,
                            Command = imagePath,
                            FilePath = filePath,
                            Publisher = publisher,
                            Category = StartupCategory.Service,
                            Location = $@"HKLM\{regPath}",
                            IsEnabled = svc.Status == ServiceControllerStatus.Running,
                            Risk = AssessRisk(svc.ServiceName, filePath, imagePath),
                            Description = svc.DisplayName,
                            StartupDelay = "On Boot"
                        });
                    }
                    catch { }
                }
            }
            catch { }
            return entries;
        }

        private string ExtractFilePath(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return "";
            command = command.Trim();
            if (command.StartsWith("\""))
            {
                var end = command.IndexOf('"', 1);
                if (end > 1) return command[1..end];
            }
            var parts = command.Split(' ');
            return parts[0];
        }

        private string ResolveShortcut(string lnkPath)
        {
            if (!lnkPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                return lnkPath;
            try
            {
                // Read the .lnk target directly from the binary header (Shell Link Binary File Format)
                // The target path starts at offset 76 if the LinkTargetIDList flag is not set,
                // but the easiest portable approach is reading the StringData section.
                // We use a minimal binary parse: skip the header and IDList, then read LocalBasePath.
                var bytes = File.ReadAllBytes(lnkPath);
                // HeaderSize = 0x4C (76), check magic
                if (bytes.Length < 76) return lnkPath;
                // LinkFlags at offset 20 (4 bytes, little-endian)
                var linkFlags = BitConverter.ToUInt32(bytes, 20);
                var hasIdList = (linkFlags & 0x01) != 0;
                int offset = 76;
                if (hasIdList)
                {
                    if (offset + 2 > bytes.Length) return lnkPath;
                    var idListSize = BitConverter.ToUInt16(bytes, offset);
                    offset += 2 + idListSize;
                }
                // LinkInfo structure
                var hasLinkInfo = (linkFlags & 0x02) != 0;
                if (hasLinkInfo && offset + 4 <= bytes.Length)
                {
                    var linkInfoSize = BitConverter.ToUInt32(bytes, offset);
                    // LocalBasePathOffset at offset+16
                    if (offset + 20 <= bytes.Length)
                    {
                        var localBasePathOffset = BitConverter.ToUInt32(bytes, offset + 16);
                        var pathStart = (int)(offset + localBasePathOffset);
                        if (pathStart < bytes.Length)
                        {
                            var pathEnd = Array.IndexOf(bytes, (byte)0, pathStart);
                            if (pathEnd > pathStart)
                            {
                                var path = System.Text.Encoding.Default.GetString(bytes, pathStart, pathEnd - pathStart);
                                if (!string.IsNullOrWhiteSpace(path)) return path;
                            }
                        }
                    }
                    offset += (int)linkInfoSize;
                }
                return lnkPath;
            }
            catch { return lnkPath; }
        }

        private string GetFilePublisher(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return "Unknown";
            try
            {
                var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(filePath);
                return string.IsNullOrWhiteSpace(info.CompanyName) ? "Unknown" : info.CompanyName;
            }
            catch { return "Unknown"; }
        }

        private string GetFileDescription(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return "";
            try
            {
                var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(filePath);
                return info.FileDescription ?? "";
            }
            catch { return ""; }
        }

        private RiskLevel AssessRisk(string name, string filePath, string command)
        {
            var publisher = GetFilePublisher(filePath);
            if (KnownSafePublishers.Contains(publisher)) return RiskLevel.Safe;
            if (!File.Exists(filePath) && !string.IsNullOrEmpty(filePath)) return RiskLevel.High;
            if (publisher == "Unknown") return RiskLevel.Unknown;
            var lName = name.ToLower();
            foreach (var kw in HighRiskKeywords)
                if (lName.Contains(kw.ToLower())) return RiskLevel.Medium;
            return RiskLevel.Low;
        }
    }
}
