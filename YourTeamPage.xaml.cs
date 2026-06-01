using System.Windows.Controls;
using LocalCursor.ViewModels;

namespace LocalCursor.Views
{
    public partial class YourTeamPage : Page
    {
        public YourTeamPage()
        {
            InitializeComponent();
            // DataContext is typically set by the Frame when navigated to, 
            // but we can ensure it's synced with the main app if needed.
            Loaded += (s, e) => {
                if (System.Windows.Application.Current.MainWindow.DataContext is MainViewModel vm)
                {
                    this.DataContext = vm;
                }
            };
        }
    }
}
