using LibreHardwareMonitor.Hardware;

namespace pawnIO_test_app.Controller
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

    public class LibrehardwareHelper
    {
        private static readonly Lazy<LibrehardwareHelper> _lazy = new(() => new LibrehardwareHelper());
        public static LibrehardwareHelper Instance => _lazy.Value;

        public Computer Computer => _computer;

        public List<FanController> Fans => _fanControllers;

        public Action DataUpdated { get; set; }

        private readonly Computer _computer;

        private readonly UpdateVisitor _updateVisitor = new();

        private bool _startGetData = false;

        private List<FanController> _fanControllers = [];

        private LibrehardwareHelper()
        {
            _computer = GetComputer();
            GetFans();
            StartGetData();
        }

        private Computer GetComputer()
        {
            try
            {
                Computer computer = new()
                {
                    IsCpuEnabled = true,
                    IsGpuEnabled = true,
                    IsMemoryEnabled = true,
                    IsMotherboardEnabled = true,
                    IsStorageEnabled = true
                };

                computer.Open();
                computer.Accept(_updateVisitor);

                return computer;
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        private void StartGetData()
        {
            _startGetData = true;
            Task.Run(() =>
            {
                while (_startGetData)
                {
                    if (_computer != null)
                    {
                        _updateVisitor.VisitComputer(_computer);
                        DataUpdated?.Invoke();
                        Thread.Sleep(500);
                    }
                }
            });
        }

        private void GetFans()
        {
            foreach (IHardware hardware in _computer.Hardware)
            {
                foreach (IHardware subhardware in hardware.SubHardware)
                {
                    _fanControllers.AddRange(ProcessSensors(subhardware));
                }

                _fanControllers.AddRange(ProcessSensors(hardware));
            }
        }

        private IEnumerable<FanController> ProcessSensors(IHardware hardware)
        {
            List<ISensor> sensors = [.. hardware.Sensors];

            var controls = sensors
                .Where(x => x.SensorType == SensorType.Control && x.Name.Contains("Fan")).ToList();

            var fans = sensors
                .Where(x => x.SensorType == SensorType.Fan && x.Name.Contains("Fan")).ToList();

            foreach (var fan in fans)
            {
                var control = controls.Find(x => x.Name == fan.Name);
                if (control != null)
                {
                    yield return CheckSensorAndPairSensors(fan, control);
                }
                else
                {
                    yield return CheckSensorAndPairSensors(fan);
                }
            }
        }

        private FanController CheckSensorAndPairSensors(ISensor rpmSensor, ISensor percentageSensor = null)
        {
            var name = rpmSensor.Name;
            var hardwareInfo = new FanController(name, percentageSensor, rpmSensor);

            return hardwareInfo;
        }

        public void SetFanToDefault()
        {
            foreach(var fan in _fanControllers)
            {
                fan.SetDefault();
            }
        }
    }
}
