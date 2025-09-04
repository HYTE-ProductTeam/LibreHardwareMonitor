using System.Windows;
using System.Windows.Threading;
using LibreHardwareMonitor.Hardware;
using pawnIO_test_app.Components;
using pawnIO_test_app.Controller;

namespace pawnIO_test_app
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly LibrehardwareHelper _helper = LibrehardwareHelper.Instance;
        private readonly Computer computer;

        public MainWindow()
        {
            InitializeComponent();

            SideMenuComp.ButtonClicked += ChangeSelectPage;

            computer = _helper.Computer;

            PlotHardwareInfo();

            //StartUpdate();
        }

        private void ChangeSelectPage(MenuButton button)
        {
            if (button == MenuButton.Info)
            {
                InfoPanel.Visibility = Visibility.Visible;
                FanPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                InfoPanel.Visibility = Visibility.Collapsed;
                FanPanel.Visibility = Visibility.Visible;
            }
        }

        private void PlotHardwareInfo()
        {
            Dispatcher.BeginInvoke(() =>
            {
                SensorPanel.Children.Clear();

                foreach (IHardware hardware in computer.Hardware)
                {
                    SensorPanel.Children.Add(new SensorDisplay(hardware.Name));

                    foreach (IHardware subhardware in hardware.SubHardware)
                    {
                        SensorDisplay newSensor = new(subhardware.Name);
                        newSensor.SetMargin(40);
                        SensorPanel.Children.Add(newSensor);

                        foreach (ISensor sensor in subhardware.Sensors)
                        {
                            newSensor = new(sensor);
                            newSensor.SetMargin(10);
                            SensorPanel.Children.Add(newSensor);
                        }
                    }

                    foreach (ISensor sensor in hardware.Sensors)
                    {
                        SensorDisplay newSensor = new(sensor);
                        newSensor.SetMargin(40);
                        SensorPanel.Children.Add(newSensor);
                    }
                }
            }, DispatcherPriority.Background);
        }

        private void StartUpdate()
        {
            Task.Run(() =>
            {
                while(true)
                {
                    PlotHardwareInfo();
                    Thread.Sleep(500);
                }
            });
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _helper.SetFanToDefault();
            computer.Close();
        }
    }
}
