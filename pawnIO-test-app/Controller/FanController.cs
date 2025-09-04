using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;

namespace pawnIO_test_app.Controller
{
    public class FanController
    {
        public string Name { get; private set; }
        public ISensor ControlSensor { get; private set; }
        public ISensor DataSensor { get; private set; }

        public FanController(string name, ISensor controlSensor, ISensor dataSensor)
        {
            Name = name;
            ControlSensor = controlSensor;
            DataSensor = dataSensor;
        }

        public void SetPercentage(int percentage)
        {
            if (ControlSensor != null)
                ControlSensor.Control.SetSoftware(percentage);
        }

        public void SetDefault()
        {
            ControlSensor?.Control.SetDefault();
        }
    }
}
