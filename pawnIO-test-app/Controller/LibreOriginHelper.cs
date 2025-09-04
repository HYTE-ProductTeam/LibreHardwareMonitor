extern alias Origin;
using Origin::LibreHardwareMonitor.Hardware;
using OriginalComputer = Origin::LibreHardwareMonitor.Hardware.Computer;

namespace pawnIO_test_app.Controller
{
    internal class LibreOriginHelper
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

        private static readonly Lazy<LibreOriginHelper> _lazy = new(() => new LibreOriginHelper());
        public static LibreOriginHelper Instance => _lazy.Value;

        public Computer Computer => _computer;

        public Action DataUpdated { get; set; }

        private readonly OriginalComputer _computer;

        private readonly UpdateVisitor _updateVisitor = new();

        private bool _startGetData = false;

        private LibreOriginHelper()
        {
            _computer = GetComputer();
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
    }
}
