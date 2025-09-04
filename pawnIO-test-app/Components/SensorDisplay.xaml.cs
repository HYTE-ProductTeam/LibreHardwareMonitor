extern alias Pawn;
extern alias Origin;
using Origin::LibreHardwareMonitor.Hardware;
using OriginalComputer = Origin::LibreHardwareMonitor.Hardware.Computer;
using Pawn::LibreHardwareMonitor.Hardware;
using PawnComputer = Pawn::LibreHardwareMonitor.Hardware.Computer;
using PawnSensor = Pawn::LibreHardwareMonitor.Hardware.ISensor;
using OriginSensor = Origin::LibreHardwareMonitor.Hardware.ISensor;
using System.Windows.Controls;
using pawnIO_test_app.Controller;
using System.Windows.Threading;

namespace pawnIO_test_app.Components
{
    /// <summary>
    /// Interaction logic for SensorDisplay.xaml
    /// </summary>
    public partial class SensorDisplay : UserControl
    {
        private readonly LibrehardwareHelper _helper = LibrehardwareHelper.Instance;
        private PawnSensor _sensor;
        private OriginSensor _origin;
        private bool _isDifferent = false;

        public SensorDisplay(string name, float? value = null, float? max = null)
        {
            InitializeComponent();
            NameTB.Text = name;
            if (value.HasValue)
            {
                ValueTB.Text = value.Value.ToString("F1");
            }
            else
            {
                ValueTB.Text = "";
            }

            if (max.HasValue)
            {
                MaxValueTB.Text = max.Value.ToString("F1");
            }
            else
            {
                MaxValueTB.Text = "";
            }
        }

        public SensorDisplay(PawnSensor sensor, OriginSensor origin)
        {
            _sensor = sensor;
            _origin = origin;
            InitializeComponent();
            NameTB.Text = sensor.Name;
            if (sensor.Value.HasValue)
            {
                //ValueTB.Text = FormatSensorValue(sensor);
                ValueTB.Text = sensor.Value.ToString();
            }
            else
            {
                ValueTB.Text = "";
            }

            if (sensor.Max.HasValue)
            {
                //MaxValueTB.Text = FormatSensorValue(sensor, true);
                MaxValueTB.Text = sensor.Max.ToString();
            }
            else
            {
                MaxValueTB.Text = "";
            }

            if (_origin.Value.HasValue)
            {
                //OriginValueTB.Text = FormatSensorValue(origin);
                OriginValueTB.Text = _origin.Value.ToString();
            }
            else
            {
                OriginValueTB.Text = "";
            }

            _helper.DataUpdated += UpdateData;
        }

        private void UpdateData()
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_sensor.Value.HasValue)
                {
                    //ValueTB.Text = FormatSensorValue(sensor);
                    ValueTB.Text = _sensor.Value.ToString();
                }
                else
                {
                    ValueTB.Text = "";
                }

                if (_sensor.Max.HasValue)
                {
                    //MaxValueTB.Text = FormatSensorValue(sensor, true);
                    MaxValueTB.Text = _sensor.Max.ToString();
                }
                else
                {
                    MaxValueTB.Text = "";
                }

                if (_origin.Value.HasValue)
                {
                    //OriginValueTB.Text = FormatSensorValue(origin);
                    OriginValueTB.Text = _origin.Value.ToString();
                }
                else
                {
                    OriginValueTB.Text = "";
                }
            }, DispatcherPriority.Background);

            CheckDifference();
        }

        private void CheckDifference()
        {
            var diff = _sensor.Value - _origin.Value;
            if (_sensor.Value != 0)
            {
                if (diff / _sensor.Value < 0.3)
                    _isDifferent = false;
                else
                    _isDifferent = true;
            }

            Dispatcher.BeginInvoke(() =>
            {
                if (_isDifferent)
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
                else
                    Background = null;
            }, DispatcherPriority.Background);
        }

        public void SetMargin(int margin)
        {
            Dispatcher.Invoke(() =>
            {
                NameTB.Margin = new System.Windows.Thickness(0, 0, margin, 0);
            });
        }

        private void Mouse_Enter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_isDifferent)
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.LightGray);
        }

        private void Mouse_Leave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_isDifferent)
                Background = null;
        }

    //    private string FormatSensorValue(ISensor sensor, bool isMaxValue = false)
    //    {
    //        if (!sensor.Value.HasValue)
    //            return "N/A";

    //        float value = isMaxValue? sensor.Max.Value : sensor.Value.Value;

    //        return sensor.SensorType switch
    //        {
    //            SensorType.Temperature => $"{value:F1}°C",
    //            SensorType.Fan => $"{value:F0} RPM",
    //            SensorType.Voltage => $"{value:F3} V",
    //            SensorType.Clock => $"{value:F0} MHz",
    //            SensorType.Load => $"{value:F1}%",
    //            SensorType.Power => $"{value:F1} W",
    //            SensorType.Data => FormatDataSize(value),
    //            SensorType.SmallData => FormatDataSize(value),
    //            SensorType.Throughput => FormatThroughput(value),
    //            SensorType.TimeSpan => FormatTimeSpan(value),
    //            SensorType.Energy => $"{value:F2} J",
    //            SensorType.Noise => $"{value:F1} dB",
    //            SensorType.Humidity => $"{value:F1}%",
    //            SensorType.Level => $"{value:F1}%",
    //            SensorType.Factor => $"{value:F3}x",
    //            SensorType.Flow => $"{value:F1} L/h",
    //            SensorType.Current => $"{value:F3} A",
    //            SensorType.Frequency => FormatFrequency(value),
    //            _ => $"{value:F2}"
    //        };
    //    }

    //    private string FormatDataSize(float sizeInGB)
    //    {
    //        if (sizeInGB >= 1024)
    //            return $"{sizeInGB / 1024:F2} TB";
    //        else if (sizeInGB >= 1)
    //            return $"{sizeInGB:F2} GB";
    //        else if (sizeInGB >= 0.001)
    //            return $"{sizeInGB * 1024:F2} MB";
    //        else
    //            return $"{sizeInGB * 1024 * 1024:F2} KB";
    //    }

    //    private string FormatThroughput(float throughputMBps)
    //    {
    //        if (throughputMBps >= 1024)
    //            return $"{throughputMBps / 1024:F2} GB/s";
    //        else if (throughputMBps >= 1)
    //            return $"{throughputMBps:F2} MB/s";
    //        else if (throughputMBps >= 0.001)
    //            return $"{throughputMBps * 1024:F2} KB/s";
    //        else
    //            return $"{throughputMBps * 1024 * 1024:F2} B/s";
    //    }

    //    private string FormatTimeSpan(float seconds)
    //    {
    //        TimeSpan timeSpan = TimeSpan.FromSeconds(seconds);
    //        if (timeSpan.TotalDays >= 1)
    //            return $"{timeSpan.TotalDays:F1} days";
    //        else if (timeSpan.TotalHours >= 1)
    //            return $"{timeSpan.TotalHours:F1} hours";
    //        else if (timeSpan.TotalMinutes >= 1)
    //            return $"{timeSpan.TotalMinutes:F1} minutes";
    //        else
    //            return $"{timeSpan.TotalSeconds:F1} seconds";
    //    }

    //    private string FormatFrequency(float frequency)
    //    {
    //        if (frequency >= 1000000)
    //            return $"{frequency / 1000000:F2} MHz";
    //        else if (frequency >= 1000)
    //            return $"{frequency / 1000:F2} kHz";
    //        else
    //            return $"{frequency:F2} Hz";
    //    }
    }
}
