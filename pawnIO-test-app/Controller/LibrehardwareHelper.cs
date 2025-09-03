using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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

        public Action DataUpdated { get; set; }

        private readonly Computer _computer;

        private readonly UpdateVisitor _updateVisitor = new();

        private bool _startGetData = false;

        private LibrehardwareHelper()
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
