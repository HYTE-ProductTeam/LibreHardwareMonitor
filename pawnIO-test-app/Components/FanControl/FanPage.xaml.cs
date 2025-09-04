using System.Windows;
using System.Windows.Controls;
using pawnIO_test_app.Controller;

namespace pawnIO_test_app.Components.FanControl
{
    /// <summary>
    /// Interaction logic for FanPage.xaml
    /// </summary>
    public partial class FanPage : UserControl
    {
        private readonly LibrehardwareHelper _librehardwareHelper = LibrehardwareHelper.Instance;

        public FanPage()
        {
            InitializeComponent();
            StackPanel panel = new StackPanel();
            for (int i = 0; i < _librehardwareHelper.Fans.Count; i++)
            {
                if (i % 2 == 0)
                {
                    panel = new();
                    panel.Orientation = Orientation.Horizontal;
                    FanPanel.Children.Add(panel);
                }

                panel.Children.Add(new FanComponent(_librehardwareHelper.Fans[i]));
            }
        }

        private void ResetFan(object sender, RoutedEventArgs e)
        {
            _librehardwareHelper.SetFanToDefault();
        }
    }
}
