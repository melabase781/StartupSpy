using StartupSpy.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace StartupSpy
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm;

        public MainWindow()
        {
            InitializeComponent();
            _vm = new MainViewModel();
            DataContext = _vm;
        }

        private void CategoryFilter_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && _vm != null)
            {
                _vm.SelectedCategory = rb.Tag?.ToString() ?? "All";
            }
        }
    }
}
