using LocalCursor.Services;
using System.Windows;
using System.Windows.Threading;

namespace LocalCursor
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private PowerManagementService _powerManagement;

        public App()
        {
            // Subscribe to the dispatcherUnhandledException event
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            _powerManagement = new PowerManagementService();
            _powerManagement.PreventSleep();

            // Create and show the main window
            MainWindow mainWindow = new MainWindow();
            mainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _powerManagement?.AllowSleep();
            _powerManagement?.Dispose();
            base.OnExit(e);
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            // Log the exception (e.g., to a file, or a logging service)
            // For demonstration, let's just show a message box
            MessageBox.Show($"An unhandled exception occurred: {e.Exception.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);

            // Prevent the application from crashing
            e.Handled = true;
        }
    }
}
