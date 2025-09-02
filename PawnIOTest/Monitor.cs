
using LibreHardwareMonitor.Hardware;

namespace PawnIOTest
{
    public class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer)
        {
            computer.Traverse(this);
        }
        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (IHardware subHardware in hardware.SubHardware) subHardware.Accept(this);
        }
        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }

    public class Monitor
    {
        public void RunMonitor()
        {
            Computer computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsMotherboardEnabled = true,
                IsControllerEnabled = true,
                IsNetworkEnabled = true,
                IsStorageEnabled = true
            };
            computer.Open();
            computer.Accept(new UpdateVisitor());

            foreach (IHardware hardware in computer.Hardware)
            {
                Console.WriteLine("Hardware: {0}", hardware.Name);

                foreach (IHardware subhardware in hardware.SubHardware)
                {
                    Console.WriteLine("\tSubhardware: {0}", subhardware.Name);
                    foreach (ISensor sensor in subhardware.Sensors)
                    {
                        string formattedValue = FormatSensorValue(sensor);
                        Console.WriteLine("\t\tSensor: {0}, value: {1}", sensor.Name, formattedValue);
                    }
                }

                foreach (ISensor sensor in hardware.Sensors)
                {
                    string formattedValue = FormatSensorValue(sensor);
                    Console.WriteLine("\tSensor: {0}, value: {1}", sensor.Name, formattedValue);
                }
            }
            computer.Close();
        }

        private string FormatSensorValue(ISensor sensor)
        {
            if (!sensor.Value.HasValue)
                return "N/A";

            float value = sensor.Value.Value;

            return sensor.SensorType switch
            {
                SensorType.Temperature => $"{value:F1}°C",
                SensorType.Fan => $"{value:F0} RPM",
                SensorType.Voltage => $"{value:F3} V",
                SensorType.Clock => $"{value:F0} MHz",
                SensorType.Load => $"{value:F1}%",
                SensorType.Power => $"{value:F1} W",
                SensorType.Data => FormatDataSize(value),
                SensorType.SmallData => FormatDataSize(value),
                SensorType.Throughput => FormatThroughput(value),
                SensorType.TimeSpan => FormatTimeSpan(value),
                SensorType.Energy => $"{value:F2} J",
                SensorType.Noise => $"{value:F1} dB",
                SensorType.Humidity => $"{value:F1}%",
                SensorType.Level => $"{value:F1}%",
                SensorType.Factor => $"{value:F3}x",
                SensorType.Flow => $"{value:F1} L/h",
                SensorType.Current => $"{value:F3} A",
                SensorType.Frequency => FormatFrequency(value),
                _ => $"{value:F2}"
            };
        }

        private string FormatDataSize(float sizeInGB)
        {
            if (sizeInGB >= 1024)
                return $"{sizeInGB / 1024:F2} TB";
            else if (sizeInGB >= 1)
                return $"{sizeInGB:F2} GB";
            else if (sizeInGB >= 0.001)
                return $"{sizeInGB * 1024:F2} MB";
            else
                return $"{sizeInGB * 1024 * 1024:F2} KB";
        }

        private string FormatThroughput(float throughputMBps)
        {
            if (throughputMBps >= 1024)
                return $"{throughputMBps / 1024:F2} GB/s";
            else if (throughputMBps >= 1)
                return $"{throughputMBps:F2} MB/s";
            else if (throughputMBps >= 0.001)
                return $"{throughputMBps * 1024:F2} KB/s";
            else
                return $"{throughputMBps * 1024 * 1024:F2} B/s";
        }

        private string FormatTimeSpan(float seconds)
        {
            TimeSpan timeSpan = TimeSpan.FromSeconds(seconds);
            if (timeSpan.TotalDays >= 1)
                return $"{timeSpan.TotalDays:F1} days";
            else if (timeSpan.TotalHours >= 1)
                return $"{timeSpan.TotalHours:F1} hours";
            else if (timeSpan.TotalMinutes >= 1)
                return $"{timeSpan.TotalMinutes:F1} minutes";
            else
                return $"{timeSpan.TotalSeconds:F1} seconds";
        }

        private string FormatFrequency(float frequency)
        {
            if (frequency >= 1000000)
                return $"{frequency / 1000000:F2} MHz";
            else if (frequency >= 1000)
                return $"{frequency / 1000:F2} kHz";
            else
                return $"{frequency:F2} Hz";
        }
    }
}
