using System.Windows.Controls;
using System.Windows.Threading;
using pawnIO_test_app.Controller;

namespace pawnIO_test_app.Components.FanControl
{
    /// <summary>
    /// Interaction logic for FanComponent.xaml
    /// </summary>
    public partial class FanComponent : UserControl
    {
        private bool _isReady = false;
        private FanController _controller;
        private readonly LibrehardwareHelper _librehardwareHelper = LibrehardwareHelper.Instance;

        public FanComponent(FanController fan)
        {
            _controller = fan;
            InitializeComponent();
            NameTB.Text = fan.Name;
            RPMTB.Text = $"{fan.DataSensor.Value} RPMs";
            PercentageTB.Text = $"{fan.ControlSensor.Value}";
            _isReady = true;
            _librehardwareHelper.DataUpdated += UpdateUI;
        }

        private void TargetSpeedChanged(object sender, TextChangedEventArgs e)
        {
            if (_isReady)
            {
                try
                {
                    int percentage = int.Parse(PercentageTB.Text);
                    if (percentage >= 0 && percentage <= 100)
                    {
                        _controller.SetPercentage(percentage);
                    }
                }
                catch { }
            }
        }

        private void UpdateUI()
        {
            Dispatcher.BeginInvoke(() =>
            {
                RPMTB.Text = $"{_controller.DataSensor.Value} RPMs";
            }, DispatcherPriority.Background);
        }
    }
}
