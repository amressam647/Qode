using System.Windows;
using System.Windows.Controls;

namespace LocalCursor.Helpers
{
    public static class PasswordBoxBinding
    {
        public static readonly DependencyProperty BoundPasswordProperty =
            DependencyProperty.RegisterAttached("BoundPassword", typeof(string), typeof(PasswordBoxBinding),
                new PropertyMetadata(string.Empty, OnBoundPasswordChanged));

        public static readonly DependencyProperty BindPasswordProperty =
            DependencyProperty.RegisterAttached("BindPassword", typeof(bool), typeof(PasswordBoxBinding),
                new PropertyMetadata(false, OnBindPasswordChanged));

        public static string GetBoundPassword(DependencyObject obj) => (string)obj.GetValue(BoundPasswordProperty);
        public static void SetBoundPassword(DependencyObject obj, string value) => obj.SetValue(BoundPasswordProperty, value);
        public static bool GetBindPassword(DependencyObject obj) => (bool)obj.GetValue(BindPasswordProperty);
        public static void SetBindPassword(DependencyObject obj, bool value) => obj.SetValue(BindPasswordProperty, value);

        private static void OnBindPasswordChanged(DependencyObject dp, DependencyPropertyChangedEventArgs e)
        {
            if (dp is PasswordBox box)
            {
                if ((bool)e.NewValue)
                {
                    box.PasswordChanged += PasswordBox_PasswordChanged;
                }
                else
                {
                    box.PasswordChanged -= PasswordBox_PasswordChanged;
                }
            }
        }

        private static void OnBoundPasswordChanged(DependencyObject dp, DependencyPropertyChangedEventArgs e)
        {
            if (dp is PasswordBox box && (bool)box.GetValue(BindPasswordProperty))
            {
                box.PasswordChanged -= PasswordBox_PasswordChanged;
                box.Password = (string)e.NewValue ?? "";
                box.PasswordChanged += PasswordBox_PasswordChanged;
            }
        }

        private static void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (sender is PasswordBox box && (bool)box.GetValue(BindPasswordProperty))
            {
                box.SetValue(BoundPasswordProperty, box.Password);
            }
        }
    }
}
