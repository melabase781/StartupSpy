using StartupSpy.Models;
using StartupSpy.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace StartupSpy.ViewModels
{
    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<object?, bool>? _canExecute;
        public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }
        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }
        public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
        public void Execute(object? parameter) => _execute(parameter);
    }

    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly StartupScannerService _scanner = new();
        private ObservableCollection<StartupEntry> _allEntries = new();
        private ObservableCollection<StartupEntry> _filteredEntries = new();
        private StartupEntry? _selectedEntry;
        private string _searchText = "";
        private string _selectedCategory = "All";
        private bool _isScanning;
        private string _statusText = "Ready — click Scan to begin";
        private int _totalCount;
        private int _highRiskCount;
        private int _unknownCount;
        private int _safeCount;

        public ObservableCollection<StartupEntry> Entries => _filteredEntries;

        public StartupEntry? SelectedEntry
        {
            get => _selectedEntry;
            set { _selectedEntry = value; OnPropertyChanged(nameof(SelectedEntry)); }
        }

        public string SearchText
        {
            get => _searchText;
            set { _searchText = value; OnPropertyChanged(nameof(SearchText)); ApplyFilter(); }
        }

        public string SelectedCategory
        {
            get => _selectedCategory;
            set { _selectedCategory = value; OnPropertyChanged(nameof(SelectedCategory)); ApplyFilter(); }
        }

        public bool IsScanning
        {
            get => _isScanning;
            set { _isScanning = value; OnPropertyChanged(nameof(IsScanning)); OnPropertyChanged(nameof(IsNotScanning)); }
        }

        public bool IsNotScanning => !_isScanning;

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(nameof(StatusText)); }
        }

        public int TotalCount { get => _totalCount; set { _totalCount = value; OnPropertyChanged(nameof(TotalCount)); } }
        public int HighRiskCount { get => _highRiskCount; set { _highRiskCount = value; OnPropertyChanged(nameof(HighRiskCount)); } }
        public int UnknownCount { get => _unknownCount; set { _unknownCount = value; OnPropertyChanged(nameof(UnknownCount)); } }
        public int SafeCount { get => _safeCount; set { _safeCount = value; OnPropertyChanged(nameof(SafeCount)); } }

        public List<string> Categories { get; } = new()
        {
            "All", "Registry", "Startup Folder", "Scheduled Task", "Service"
        };

        public ICommand ScanCommand { get; }
        public ICommand OpenLocationCommand { get; }
        public ICommand CopyPathCommand { get; }
        public ICommand FilterHighRiskCommand { get; }
        public ICommand ToggleEntryCommand { get; }

        public MainViewModel()
        {
            ScanCommand = new RelayCommand(_ => RunScan(), _ => IsNotScanning);
            OpenLocationCommand = new RelayCommand(_ => OpenFileLocation(), _ => SelectedEntry != null);
            CopyPathCommand = new RelayCommand(_ => CopyPath(), _ => SelectedEntry != null);
            ToggleEntryCommand = new RelayCommand(_ => ToggleEntry(), _ => SelectedEntry != null);
            FilterHighRiskCommand = new RelayCommand(_ =>
            {
                SelectedCategory = "All";
                SearchText = "";
                var high = _allEntries.Where(e => e.Risk == RiskLevel.High || e.Risk == RiskLevel.Medium).ToList();
                _filteredEntries.Clear();
                foreach (var e in high) _filteredEntries.Add(e);
                StatusText = $"Showing {high.Count} elevated-risk entries";
            });
        }

        private async void RunScan()
        {
            IsScanning = true;
            StatusText = "Scanning startup entries...";
            _allEntries.Clear();
            _filteredEntries.Clear();
            SelectedEntry = null;

            try
            {
                var results = await Task.Run(() => _scanner.ScanAll());
                _allEntries = new ObservableCollection<StartupEntry>(results.OrderByDescending(e => (int)e.Risk));
                ApplyFilter();
                UpdateStats();
                StatusText = $"Scan complete — {_allEntries.Count} startup entries found";
            }
            catch (Exception ex)
            {
                StatusText = $"Scan error: {ex.Message}";
                MessageBox.Show($"Error scanning: {ex.Message}", "StartupSpy", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                IsScanning = false;
            }
        }

        private void ApplyFilter()
        {
            var query = _allEntries.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                var s = SearchText.ToLower();
                query = query.Where(e =>
                    e.Name.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                    e.Publisher.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                    e.FilePath.Contains(s, StringComparison.OrdinalIgnoreCase));
            }

            if (SelectedCategory != "All")
            {
                query = query.Where(e => e.CategoryLabel == SelectedCategory);
            }

            _filteredEntries.Clear();
            foreach (var e in query) _filteredEntries.Add(e);
        }

        private void UpdateStats()
        {
            TotalCount = _allEntries.Count;
            HighRiskCount = _allEntries.Count(e => e.Risk == RiskLevel.High);
            UnknownCount = _allEntries.Count(e => e.Risk == RiskLevel.Unknown);
            SafeCount = _allEntries.Count(e => e.Risk == RiskLevel.Safe);
        }

        private void OpenFileLocation()
        {
            if (SelectedEntry == null) return;
            try
            {
                var path = SelectedEntry.FilePath;
                if (System.IO.File.Exists(path))
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
                else
                    MessageBox.Show("File not found on disk.", "StartupSpy", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch { }
        }

        private void ToggleEntry()
        {
            if (SelectedEntry == null) return;
            try
            {
                bool success = _scanner.ToggleEntry(SelectedEntry);
                if (success)
                {
                    SelectedEntry.IsEnabled = !SelectedEntry.IsEnabled;
                    StatusText = $"{SelectedEntry.Name} {(SelectedEntry.IsEnabled ? "enabled" : "disabled")}";
                }
                else
                {
                    MessageBox.Show(
                        $"Could not toggle '{SelectedEntry.Name}'.\n\nThis may require Administrator privileges, or this entry type does not support toggling.",
                        "StartupSpy", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "StartupSpy", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CopyPath()
        {
            if (SelectedEntry == null) return;
            Clipboard.SetText(SelectedEntry.FilePath);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
