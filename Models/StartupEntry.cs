using System.ComponentModel;

namespace StartupSpy.Models
{
    public enum StartupCategory
    {
        Registry,
        StartupFolder,
        ScheduledTask,
        Service
    }

    public enum RiskLevel
    {
        Safe,
        Low,
        Medium,
        High,
        Unknown
    }

    public class StartupEntry : INotifyPropertyChanged
    {
        private bool _isEnabled;

        public string Name { get; set; } = "";
        public string Publisher { get; set; } = "";
        public string FilePath { get; set; } = "";
        public string Command { get; set; } = "";
        public StartupCategory Category { get; set; }
        public RiskLevel Risk { get; set; }
        public string Location { get; set; } = "";
        public bool IsEnabled
        {
            get => _isEnabled;
            set { _isEnabled = value; OnPropertyChanged(nameof(IsEnabled)); }
        }
        public string Description { get; set; } = "";
        public string StartupDelay { get; set; } = "None";

        public string CategoryLabel => Category switch
        {
            StartupCategory.Registry => "Registry",
            StartupCategory.StartupFolder => "Startup Folder",
            StartupCategory.ScheduledTask => "Scheduled Task",
            StartupCategory.Service => "Service",
            _ => "Unknown"
        };

        public string RiskLabel => Risk switch
        {
            RiskLevel.Safe => "Safe",
            RiskLevel.Low => "Low",
            RiskLevel.Medium => "Medium",
            RiskLevel.High => "High",
            _ => "Unknown"
        };

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
