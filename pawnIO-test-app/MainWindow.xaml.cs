extern alias Pawn;
extern alias Origin;
using Origin::LibreHardwareMonitor.Hardware;
using OriginalComputer = Origin::LibreHardwareMonitor.Hardware.Computer;
using Pawn::LibreHardwareMonitor.Hardware;
using PawnComputer = Pawn::LibreHardwareMonitor.Hardware.Computer;
using PawnSensor = Pawn::LibreHardwareMonitor.Hardware.ISensor;
using OriginSensor = Origin::LibreHardwareMonitor.Hardware.ISensor;
using System.Windows;
using System.Windows.Threading;
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
        private readonly LibreOriginHelper _originHelper = LibreOriginHelper.Instance;
        private readonly PawnComputer computer;
        private readonly OriginalComputer originComputer;

        public MainWindow()
        {
            InitializeComponent();
            computer = _helper.Computer;
            originComputer = _originHelper.Computer;
            PlotHardwareInfo();

            //StartUpdate();
        }

        private void PlotHardwareInfo()
        {
            Dispatcher.BeginInvoke(() =>
            {
                SensorPanel.Children.Clear();

                for (int i = 0; i < computer.Hardware.Count; i++)
                {
                    var hardware = computer.Hardware[i];
                    SensorPanel.Children.Add(new SensorDisplay(hardware.Name));

                    for (int j = 0; j < hardware.SubHardware.Length; j++)
                    {
                        var subHardware = hardware.SubHardware[j];
                        SensorDisplay newSensor = new(subHardware.Name);
                        newSensor.SetMargin(40);
                        SensorPanel.Children.Add(newSensor);

                        for (int x = 0; x < subHardware.Sensors.Length; x++)
                        {
                            PawnSensor sensor = subHardware.Sensors[x];
                            var origin = originComputer.Hardware[i].SubHardware[j].Sensors[x];
                            newSensor = new(sensor, origin);
                            newSensor.SetMargin(10);
                            SensorPanel.Children.Add(newSensor);
                        }
                    }

                    for (int x = 0; x < hardware.Sensors.Length; x++)
                    {
                        var sensor = hardware.Sensors[x];
                        var origin = originComputer.Hardware[i].Sensors[x];
                        SensorDisplay newSensor = new(sensor, origin);
                        newSensor.SetMargin(10);
                        SensorPanel.Children.Add(newSensor);
                    }
                }




                //foreach (IHardware hardware in computer.Hardware)
                //{
                //    SensorPanel.Children.Add(new SensorDisplay(hardware.Name));

                //    foreach (IHardware subhardware in hardware.SubHardware)
                //    {
                //        SensorDisplay newSensor = new(subhardware.Name);
                //        newSensor.SetMargin(40);
                //        SensorPanel.Children.Add(newSensor);

                //        foreach (ISensor sensor in subhardware.Sensors)
                //        {

                //            newSensor = new(sensor);
                //            newSensor.SetMargin(10);
                //            SensorPanel.Children.Add(newSensor);
                //        }
                //    }

                //    foreach (ISensor sensor in hardware.Sensors)
                //    {
                //        SensorDisplay newSensor = new(sensor);
                //        newSensor.SetMargin(40);
                //        SensorPanel.Children.Add(newSensor);
                //    }
                //}
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
    }
}
