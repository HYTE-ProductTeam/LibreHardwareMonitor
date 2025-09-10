// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LibreHardwareMonitor.Hardware.Cpu;

internal sealed class Amd17Cpu : AmdCpu
{
    private readonly Processor _processor;
    private readonly Dictionary<SensorType, int> _sensorTypeIndex;
    private readonly RyzenSMU _smu;

    public Amd17Cpu(int processorIndex, CpuId[][] cpuId, ISettings settings) : base(processorIndex, cpuId, settings)
    {
        _sensorTypeIndex = new Dictionary<SensorType, int>();
        foreach (SensorType type in Enum.GetValues(typeof(SensorType)))
        {
            _sensorTypeIndex.Add(type, 0);
        }

        _sensorTypeIndex[SensorType.Load] = _active.Count(x => x.SensorType == SensorType.Load);

        _smu = new RyzenSMU(_family, _model, _packageType);

        // Add all numa nodes.
        // Register ..1E_2, [10:8] + 1
        _processor = new Processor(this);

        // Add all numa nodes.
        int coreId = 0;
        int lastCoreId = -1; // Invalid id.

        // Ryzen 3000's skip some core ids.
        // So start at 1 and count upwards when the read core changes.
        foreach (CpuId[] cpu in cpuId.OrderBy(x => x[0].ExtData[0x1e, 1] & 0xFF))
        {
            CpuId thread = cpu[0];

            // CPUID_Fn8000001E_EBX, Register ..1E_1, [7:0]
            // threads per core =  CPUID_Fn8000001E_EBX[15:8] + 1
            // CoreId: core ID =  CPUID_Fn8000001E_EBX[7:0]
            int coreIdRead = (int)(thread.ExtData[0x1e, 1] & 0xff);

            // CPUID_Fn8000001E_ECX, Node Identifiers, Register ..1E_2
            // NodesPerProcessor =  CPUID_Fn8000001E_ECX[10:8]
            // nodeID =  CPUID_Fn8000001E_ECX[7:0]
            int nodeId = (int)(thread.ExtData[0x1e, 2] & 0xff);

            if (coreIdRead != lastCoreId)
            {
                coreId++;
            }

            lastCoreId = coreIdRead;

            _processor.AppendThread(thread, nodeId, coreId);
        }

        Update();
    }

    protected override uint[] GetMsrs()
    {
        return new[] { PERF_CTL_0, PERF_CTR_0, HWCR, MSR_PSTATE_0, COFVID_STATUS };
    }

    public override string GetReport()
    {
        StringBuilder r = new();
        r.Append(base.GetReport());
        r.Append(_smu.GetReport());
        return r.ToString();
    }

    public override void Update()
    {
        base.Update();

        _processor.UpdateSensors();

        foreach (NumaNode node in _processor.Nodes)
        {
            NumaNode.UpdateSensors();

            foreach (Core c in node.Cores)
            {
                c.UpdateSensors();
            }
        }

        _processor.UpdateVirtualSensor();
    }

    private class Processor
    {
        private readonly Sensor _busClock;
        private readonly Sensor _avgClock;
        private readonly Sensor _avgClockEffcetive;

        private readonly Sensor[] _ccdTemperatures;
        private readonly Sensor _coreTemperatureTctl;
        private readonly Sensor _coreTemperatureTctlTdie;
        private readonly Sensor _coreTemperatureTdie;
        private readonly Sensor _coreVoltage;
        private readonly Amd17Cpu _cpu;
        private readonly Sensor _packagePower;
        private readonly Dictionary<KeyValuePair<uint, RyzenSMU.SmuSensorType>, Sensor> _smuSensors = new();
        private readonly Sensor _socVoltage;

        private Sensor _ccdsAverageTemperature;
        private Sensor _ccdsMaxTemperature;
        private DateTime _lastSampleTime = new(0);
        private uint _lastPwrValue;

        public Processor(Hardware hardware)
        {
            _cpu = (Amd17Cpu)hardware;

            _packagePower = new Sensor("Package", _cpu._sensorTypeIndex[SensorType.Power]++, SensorType.Power, _cpu, _cpu._settings);
            _coreTemperatureTctl = new Sensor("Core (Tctl)", _cpu._sensorTypeIndex[SensorType.Temperature]++, SensorType.Temperature, _cpu, _cpu._settings);
            _coreTemperatureTdie = new Sensor("Core (Tdie)", _cpu._sensorTypeIndex[SensorType.Temperature]++, SensorType.Temperature, _cpu, _cpu._settings);
            _coreTemperatureTctlTdie = new Sensor("Core (Tctl/Tdie)", _cpu._sensorTypeIndex[SensorType.Temperature]++, SensorType.Temperature, _cpu, _cpu._settings);
            _ccdTemperatures = new Sensor[8]; // Hardcoded until there's a way to get max CCDs.
            _coreVoltage = new Sensor("Core (SVI2 TFN)", _cpu._sensorTypeIndex[SensorType.Voltage]++, SensorType.Voltage, _cpu, _cpu._settings);
            _socVoltage = new Sensor("SoC (SVI2 TFN)", _cpu._sensorTypeIndex[SensorType.Voltage]++, SensorType.Voltage, _cpu, _cpu._settings);
            _busClock = new Sensor("Bus Speed", _cpu._sensorTypeIndex[SensorType.Clock]++, SensorType.Clock, _cpu, _cpu._settings);
            _avgClock = new Sensor("Cores (Average)", _cpu._sensorTypeIndex[SensorType.Clock]++, SensorType.Clock, _cpu, _cpu._settings);
            _avgClockEffcetive = new Sensor("Cores (Average Effective)", _cpu._sensorTypeIndex[SensorType.Clock]++, SensorType.Clock, _cpu, _cpu._settings);

            _cpu.ActivateSensor(_packagePower);
            _cpu.ActivateSensor(_avgClock);
            _cpu.ActivateSensor(_avgClockEffcetive);

            foreach (KeyValuePair<uint, RyzenSMU.SmuSensorType> sensor in _cpu._smu.GetPmTableStructure())
            {
                _smuSensors.Add(sensor, new Sensor(sensor.Value.Name, _cpu._sensorTypeIndex[sensor.Value.Type]++, sensor.Value.Type, _cpu, _cpu._settings));
            }
        }

        public List<NumaNode> Nodes { get; } = new();

        public void UpdateSensors()
        {
            NumaNode node = Nodes[0];
            Core core = node?.Cores[0];
            CpuId cpuId = core?.Threads.FirstOrDefault()?.Cpu;

            if (cpuId == null)
                return;

            GroupAffinity previousAffinity = ThreadAffinity.Set(cpuId.Affinity);

            // MSRC001_0299
            // TU [19:16]
            // ESU [12:8] -> Unit 15.3 micro Joule per increment (default), 1/2^ESU micro Joule
            // PU [3:0]
            Ring0.ReadMsr(MSR_PWR_UNIT, out uint eax, out uint _);
            int esu = (int)((eax >> 8) & 0x1F);
            double energyBaseUnit = Math.Pow(0.5,esu);


            // MSRC001_029B
            // total_energy [31:0]
            DateTime sampleTime = DateTime.UtcNow;
            Ring0.ReadMsr(MSR_PKG_ENERGY_STAT, out eax, out _);

            uint totalEnergy = eax;

            bool gotTempFromSmu = false;
            bool gotPkgFromSmu = false;

            float[] smuData = _cpu._smu.GetPmTable();
            if (smuData != null && smuData.Length > 0)
            {
                // ---- Tdie ----
                var tdieKvp = _cpu._smu.GetPmTableStructure()
                    .FirstOrDefault(kv =>
                        kv.Value.Type == SensorType.Temperature &&
                        !string.IsNullOrEmpty(kv.Value.Name) &&
                        (kv.Value.Name.Contains("Tdie") ||
                         kv.Value.Name.Contains("Tctl/Tdie")));

                if (!string.IsNullOrEmpty(tdieKvp.Value.Name) && tdieKvp.Key < smuData.Length)
                {
                    float t = smuData[tdieKvp.Key] * tdieKvp.Value.Scale;
                    _coreTemperatureTdie.Value = t;
                    _cpu.ActivateSensor(_coreTemperatureTdie);

                    _coreTemperatureTctlTdie.Value = t;
                    _cpu.ActivateSensor(_coreTemperatureTctlTdie);

                    gotTempFromSmu = true;
                }

                // ---- Package Power ----
                var pkgKvp = _cpu._smu.GetPmTableStructure()
                    .Where(kv => kv.Value.Type == SensorType.Power && !string.IsNullOrEmpty(kv.Value.Name))
                    .OrderByDescending(kv =>
                        (kv.Value.Name.Contains("Package") ? 3 : 0) +
                        (kv.Value.Name.Contains("Socket") ? 2 : 0) +
                        (kv.Value.Name.Contains("CPU") ? 1 : 0))
                    .FirstOrDefault();

                if (!string.IsNullOrEmpty(pkgKvp.Value.Name) && pkgKvp.Key < smuData.Length)
                {
                    float w = smuData[pkgKvp.Key] * pkgKvp.Value.Scale;
                    _packagePower.Value = w;
                    _cpu.ActivateSensor(_packagePower);
                    gotPkgFromSmu = true;
                }
            }

            uint smuSvi0Tfn = 0;
            uint smuSvi0TelPlane0 = 0;
            uint smuSvi0TelPlane1 = 0;

            if (Mutexes.WaitPciBus(10))
            {
                // 只有當 SMU 沒給溫度時，才用 THM 讀 Tctl/Tdie
                uint temperature = 0;
                if (!gotTempFromSmu)
                {
                    // THM_TCON_CUR_TMP: CUR_TEMP [31:21]
                    Ring0.WritePciConfig(0x00, FAMILY_17H_PCI_CONTROL_REGISTER, F17H_M01H_THM_TCON_CUR_TMP);
                    Ring0.ReadPciConfig(0x00, FAMILY_17H_PCI_CONTROL_REGISTER + 4, out temperature);
                }

                // 依然讀 SVI（電壓要用），不管溫度來源
                Ring0.WritePciConfig(0x00, FAMILY_17H_PCI_CONTROL_REGISTER, F17H_M01H_SVI + 0x8);
                Ring0.ReadPciConfig(0x00, FAMILY_17H_PCI_CONTROL_REGISTER + 4, out smuSvi0Tfn);

                bool supportsPerCcdTemperatures = false;
                uint sviPlane0Offset;
                uint sviPlane1Offset;
                switch (cpuId.Model)
                {
                    case 0x31: // Threadripper 3000.
                        sviPlane0Offset = F17H_M01H_SVI + 0x14;
                        sviPlane1Offset = F17H_M01H_SVI + 0x10;
                        supportsPerCcdTemperatures = true;
                        break;

                    case 0x71: // Zen 2.
                    case 0x21: // Zen 3.
                        sviPlane0Offset = F17H_M01H_SVI + 0x10;
                        sviPlane1Offset = F17H_M01H_SVI + 0xC;
                        supportsPerCcdTemperatures = true;
                        break;

                    case 0x61: // Zen 4
                    case 0x44: // Zen 5
                        sviPlane0Offset = F17H_M01H_SVI + 0x10;
                        sviPlane1Offset = F17H_M01H_SVI + 0xC;
                        supportsPerCcdTemperatures = true;
                        break;

                    default: // Zen and Zen+.
                        sviPlane0Offset = F17H_M01H_SVI + 0xC;
                        sviPlane1Offset = F17H_M01H_SVI + 0x10;
                        break;
                }

                // SVI TEL
                Ring0.WritePciConfig(0x00, FAMILY_17H_PCI_CONTROL_REGISTER, sviPlane0Offset);
                Ring0.ReadPciConfig(0x00, FAMILY_17H_PCI_CONTROL_REGISTER + 4, out smuSvi0TelPlane0);

                Ring0.WritePciConfig(0x00, FAMILY_17H_PCI_CONTROL_REGISTER, sviPlane1Offset);
                Ring0.ReadPciConfig(0x00, FAMILY_17H_PCI_CONTROL_REGISTER + 4, out smuSvi0TelPlane1);

                ThreadAffinity.Set(previousAffinity);

                // === 封裝功耗（MSR 能量累積）—只有在沒拿到 SMU 值時才用 ===
                TimeSpan deltaTime = sampleTime - _lastSampleTime;
                if (_lastSampleTime.Ticks == 0)
                {
                    deltaTime = new(0);
                    _lastSampleTime = sampleTime;
                    _lastPwrValue = totalEnergy;
                }
                _lastSampleTime = sampleTime;

                long pwr;
                if (_lastPwrValue <= totalEnergy)
                    pwr = totalEnergy - _lastPwrValue;
                else
                    pwr = (0xffffffff - _lastPwrValue) + totalEnergy;

                _lastPwrValue = totalEnergy;

                if (!gotPkgFromSmu && deltaTime.Ticks > 0)
                {
                    double energy = energyBaseUnit * pwr; // microJ/increment 轉為 J/s
                    energy /= deltaTime.TotalSeconds;

                    if (!double.IsNaN(energy))
                    {
                        _packagePower.Value = (float)energy;
                        _cpu.ActivateSensor(_packagePower);
                    }
                }

                // === 溫度（PCI THM 路徑）—只有在沒拿到 SMU 值時才用 ===
                if (!gotTempFromSmu)
                {
                    bool tempOffsetFlag = (temperature & F17H_TEMP_OFFSET_FLAG) != 0;
                    temperature = (temperature >> 21) * 125;

                    float offset = 0.0f;
                    if (string.IsNullOrWhiteSpace(cpuId.Name))
                        offset = 0;
                    else if (cpuId.Name.Contains("1600X") || cpuId.Name.Contains("1700X") || cpuId.Name.Contains("1800X"))
                        offset = -20.0f;
                    else if (cpuId.Name.Contains("Threadripper 19") || cpuId.Name.Contains("Threadripper 29"))
                        offset = -27.0f;
                    else if (cpuId.Name.Contains("2700X"))
                        offset = -10.0f;

                    float t = temperature * 0.001f;
                    if (tempOffsetFlag)
                        t += -49.0f;

                    if (offset < 0)
                    {
                        _coreTemperatureTctl.Value = t;
                        _coreTemperatureTdie.Value = t + offset;

                        _cpu.ActivateSensor(_coreTemperatureTctl);
                        _cpu.ActivateSensor(_coreTemperatureTdie);
                    }
                    else
                    {
                        _coreTemperatureTctlTdie.Value = t;
                        _cpu.ActivateSensor(_coreTemperatureTctlTdie);
                    }
                }

                // CCD 溫度（仍走 PCI，SMU 有也可共存；此邏輯保留）
                if (supportsPerCcdTemperatures)
                {
                    for (uint i = 0; i < _ccdTemperatures.Length; i++)
                    {
                        if (cpuId.Model is 0x61 or 0x44) // Raphael or GraniteRidge
                            Ring0.WritePciConfig(0x00, FAMILY_17H_PCI_CONTROL_REGISTER, F17H_M61H_CCD1_TEMP + (i * 0x4));
                        else
                            Ring0.WritePciConfig(0x00, FAMILY_17H_PCI_CONTROL_REGISTER, F17H_M70H_CCD1_TEMP + (i * 0x4));

                        Ring0.ReadPciConfig(0x00, FAMILY_17H_PCI_CONTROL_REGISTER + 4, out uint ccdRawTemp);

                        ccdRawTemp &= 0xFFF;
                        float ccdTemp = ((ccdRawTemp * 125) - 305000) * 0.001f;
                        if (ccdRawTemp > 0 && ccdTemp < 125)
                        {
                            if (_ccdTemperatures[i] == null)
                            {
                                _cpu.ActivateSensor(_ccdTemperatures[i] = new Sensor($"CCD{i + 1} (Tdie)",
                                                                                     _cpu._sensorTypeIndex[SensorType.Temperature]++,
                                                                                     SensorType.Temperature,
                                                                                     _cpu,
                                                                                     _cpu._settings));
                            }
                            _ccdTemperatures[i].Value = ccdTemp;
                        }
                    }

                    Sensor[] activeCcds = _ccdTemperatures.Where(x => x != null).ToArray();
                    if (activeCcds.Length > 1)
                    {
                        if (_ccdsMaxTemperature == null)
                        {
                            _cpu.ActivateSensor(_ccdsMaxTemperature = new Sensor("CCDs Max (Tdie)",
                                                                                 _cpu._sensorTypeIndex[SensorType.Temperature]++,
                                                                                 SensorType.Temperature,
                                                                                 _cpu,
                                                                                 _cpu._settings));
                        }
                        if (_ccdsAverageTemperature == null)
                        {
                            _cpu.ActivateSensor(_ccdsAverageTemperature = new Sensor("CCDs Average (Tdie)",
                                                                                     _cpu._sensorTypeIndex[SensorType.Temperature]++,
                                                                                     SensorType.Temperature,
                                                                                     _cpu,
                                                                                     _cpu._settings));
                        }

                        _ccdsMaxTemperature.Value = activeCcds.Max(x => x.Value);
                        _ccdsAverageTemperature.Value = activeCcds.Average(x => x.Value);
                    }
                }

                Mutexes.ReleasePciBus();
            }

            // voltage
            const double vidStep = 0.00625;
            double vcc;
            uint svi0PlaneXVddCor;

            if (cpuId.Model is 0x61 or 0x44) // Readout not working for Ryzen 7000/9000.
                smuSvi0Tfn |= 0x01 | 0x02;

            // Core (0x01).
            if ((smuSvi0Tfn & 0x01) == 0)
            {
                svi0PlaneXVddCor = (smuSvi0TelPlane0 >> 16) & 0xff;
                vcc = 1.550 - (vidStep * svi0PlaneXVddCor);
                _coreVoltage.Value = (float)vcc;

                _cpu.ActivateSensor(_coreVoltage);
            }

            // SoC (0x02), not every Zen cpu has this voltage.
            if (cpuId.Model is 0x11 or 0x21 or 0x71 or 0x31 || (smuSvi0Tfn & 0x02) == 0)
            {
                svi0PlaneXVddCor = (smuSvi0TelPlane1 >> 16) & 0xff;
                vcc = 1.550 - (vidStep * svi0PlaneXVddCor);
                _socVoltage.Value = (float)vcc;

                _cpu.ActivateSensor(_socVoltage);
            }

            double timeStampCounterMultiplier = GetTimeStampCounterMultiplier();
            if (timeStampCounterMultiplier > 0)
            {
                _busClock.Value = (float)(_cpu.TimeStampCounterFrequency / timeStampCounterMultiplier);
                _cpu.ActivateSensor(_busClock);
            }

            if (_cpu._smu.IsPmTableLayoutDefined())
            {
                smuData = _cpu._smu.GetPmTable();

                foreach (KeyValuePair<KeyValuePair<uint, RyzenSMU.SmuSensorType>, Sensor> sensor in _smuSensors)
                {
                    if (smuData.Length > sensor.Key.Key)
                    {
                        sensor.Value.Value = smuData[sensor.Key.Key] * sensor.Key.Value.Scale;
                        if (sensor.Value.Value != 0)
                            _cpu.ActivateSensor(sensor.Value);
                    }
                }
            }            
        }

        public void UpdateVirtualSensor()
        {
            if (Nodes == null || Nodes.Count == 0)
                return;

            double clock = Nodes.Average(x => x.CoreClock);
            _avgClock.Value = (float)Math.Round(clock, 0);

            clock = Nodes.Average(x => x.EffectiveClock);
            _avgClockEffcetive.Value = (float)Math.Round(clock, 0);
        }

        private double GetTimeStampCounterMultiplier()
        {
            Ring0.ReadMsr(MSR_PSTATE_0, out uint eax, out _);

            if (_cpu._family == 0x1a)
            {
                //zen 5
                uint cpuFid = eax & 0xfff;
                return (cpuFid * 5) / 100.0;
            }
            else
            {
                uint cpuDfsId = (eax >> 8) & 0x3f;
                uint cpuFid = eax & 0xff;
                return 2.0 * cpuFid / cpuDfsId;
            }
        }

        public void AppendThread(CpuId thread, int numaId, int coreId)
        {
            NumaNode node = null;
            foreach (NumaNode n in Nodes)
            {
                if (n.NodeId == numaId)
                {
                    node = n;
                    break;
                }
            }

            if (node == null)
            {
                node = new NumaNode(_cpu, numaId);
                Nodes.Add(node);
            }

            if (thread != null)
                node.AppendThread(thread, coreId);
        }
    }

    private class NumaNode
    {
        private readonly Amd17Cpu _cpu;

        public NumaNode(Amd17Cpu cpu, int id)
        {
            Cores = new List<Core>();
            NodeId = id;
            _cpu = cpu;
        }

        public List<Core> Cores { get; }

        public int NodeId { get; }

        public double CoreClock
        {
            get
            {
                if(Cores == null)
                    return 0;

                return Cores.Average(x => x.CoreClock);
            }
        }

        public double EffectiveClock
        {
            get
            {
                if (Cores == null)
                    return 0;

                return Cores.Average(x => x.EffectiveClock);
            }
        }


        public void AppendThread(CpuId thread, int coreId)
        {
            Core core = null;
            foreach (Core c in Cores)
            {
                if (c.CoreId == coreId)
                    core = c;
            }

            if (core == null)
            {
                core = new Core(_cpu, coreId);
                Cores.Add(core);
            }

            if (thread != null)
                core.AppedThread(thread);
        }

        public static void UpdateSensors()
        { }
    }

    private class CpuThread
    {
        private DateTime _sampleTime = new(0);
        private DateTime _lastSampleTime = new(0);
        private ulong _mperf = 0;
        private ulong _aperf = 0;
        private ulong _mperfLast = 0;
        private ulong _aperfLast = 0;
        private ulong _mperfDelta = 0;
        private ulong _aperfDelta = 0;

        private CpuId _cpu;
        public CpuId Cpu { get { return _cpu; } }

        public TimeSpan SampleDuration { get; private set; }= TimeSpan.Zero;
        public double EffectiveClock { get; private set; } = 0;

        public ulong MperfDelta { get {  return _mperfDelta; } }
        public ulong AperfDelta { get { return _aperfDelta; } }

        public CpuThread(CpuId cpu)
        {
            _cpu = cpu;
        }

        public void ReadPerformanceCounter()
        {
            ThreadAffinity.Set(Cpu.Affinity);

            _sampleTime = DateTime.UtcNow;

            // performance counter
            // MSRC000_00E7, P0 state counter
            Ring0.ReadMsr(MSR_MPERF_RO, out ulong edxeax);
            _mperf = edxeax;
            // MSRC000_00E8, C0 state counter
            Ring0.ReadMsr(MSR_APERF_RO, out edxeax);
            _aperf = edxeax;
        }

        public void UpdateMeasurements()
        {
            if (_mperf < _mperfLast || _aperf < _aperfLast)
            {
                // current measurment is invalid when _mperf or _aperf overflow
                _lastSampleTime = new(0);
            }

            if (_lastSampleTime.Ticks == 0)
            {
                _lastSampleTime = _sampleTime;
                _mperfLast = _mperf;
                _aperfLast = _aperf;

                _mperfDelta = 0;
                _aperfDelta = 0;
                return;
            }

            SampleDuration = _sampleTime - _lastSampleTime;
            _lastSampleTime = _sampleTime;

            _mperfDelta = _mperf - _mperfLast;
            _aperfDelta = _aperf - _aperfLast;
            _mperfLast = _mperf;
            _aperfLast = _aperf;

            if (_mperfDelta > 20000e6)
                _mperfDelta = 0;
            if (_aperfDelta > 20000e6)
                _aperfDelta = 0;

            if(_aperfDelta == 0 || _mperfDelta == 0)
            {
                //overflow possible, numbers are > 20 GHz
                _lastSampleTime = new(0);
                return;
            }

            //effective clock
            double freq = (double)_aperfDelta / (SampleDuration.TotalMilliseconds * 1000.0);
            EffectiveClock = Math.Round(freq);
        }

        public bool HasValidCounters()
        {
            return _mperfDelta > 0 && _aperfDelta > 0 && SampleDuration.Ticks > 0;
        }
    }

    private class Core
    {
        private readonly Sensor _clock;
        private readonly Sensor _clockEffective;
        private readonly Amd17Cpu _cpu;
        private readonly Sensor _multiplier;
        private readonly Sensor _power;
        private readonly Sensor _vcore;
        private ISensor _busSpeed;
        private DateTime _lastSampleTime = new(0);
        private uint _lastPwrValue = 0;

        public double CoreClock { get; set; } = 0;
        public double EffectiveClock { get; set; } = 0;

        public Core(Amd17Cpu cpu, int id)
        {
            _cpu = cpu;
            CoreId = id;
            _clock = new Sensor("Core #" + CoreId, _cpu._sensorTypeIndex[SensorType.Clock]++, SensorType.Clock, cpu, cpu._settings);
            _clockEffective = new Sensor("Core #" + CoreId + " (Effective)", _cpu._sensorTypeIndex[SensorType.Clock]++, SensorType.Clock, cpu, cpu._settings);
            _multiplier = new Sensor("Core #" + CoreId, cpu._sensorTypeIndex[SensorType.Factor]++, SensorType.Factor, cpu, cpu._settings);
            _power = new Sensor("Core #" + CoreId + " (SMU)", cpu._sensorTypeIndex[SensorType.Power]++, SensorType.Power, cpu, cpu._settings);
            _vcore = new Sensor("Core #" + CoreId + " VID", cpu._sensorTypeIndex[SensorType.Voltage]++, SensorType.Voltage, cpu, cpu._settings);

            cpu.ActivateSensor(_clock);
            cpu.ActivateSensor(_clockEffective);
            cpu.ActivateSensor(_multiplier);
            cpu.ActivateSensor(_power);
            cpu.ActivateSensor(_vcore);
        }

        public int CoreId { get; }

        public List<CpuThread> Threads { get; } = new List<CpuThread>();

        public void AppedThread(CpuId cpu)
        {
            CpuThread t = new CpuThread(cpu);
            Threads.Add(t);
        }

        public void UpdateSensors()
        {
            if (Threads.Count == 0)
                return;

            CpuThread thread = Threads[0];
            GroupAffinity previousAffinity = ThreadAffinity.Set(thread.Cpu.Affinity);

            // MSRC001_0299
            // TU [19:16]
            // ESU [12:8] -> Unit 15.3 micro Joule per increment (default), 1/2^ESU micro Joule
            // PU [3:0]
            Ring0.ReadMsr(MSR_PWR_UNIT, out uint eax, out uint _);
            int esu = (int)((eax >> 8) & 0x1F);
            double energyBaseUnit = Math.Pow(0.5, esu);

            // MSRC001_029A
            // total_energy [31:0]
            DateTime sampleTime = DateTime.UtcNow;
            Ring0.ReadMsr(MSR_CORE_ENERGY_STAT, out eax, out _);
            uint totalEnergy = eax;

            // MSRC001_0293
            // CurHwPstate [24:22]
            // CurCpuVid [21:14]
            // CurCpuDfsId [13:8]
            // CurCpuFid [7:0] zen1..4
            // CurCpuFid [11:0] zen5
            Ring0.ReadMsr(MSR_HARDWARE_PSTATE_STATUS, out eax, out _);
            uint msrPstate = eax;
            int curCpuVid = (int)((eax >> 14) & 0xff);

            foreach(var t in Threads)
            {
                t.ReadPerformanceCounter();
            }

            // MSRC001_0063[P - state Status](PStateStat)
            // Ring0.ReadMsr(MSR_PSTATE_STATUS, out eax, out _);
            // int curPstateStaus = (int)(eax & 0x7);

            // MSRC001_0064 + x
            // PstateEn[63], 1 == enabled
            // IddDiv [31:30]
            // IddValue [29:22]
            // CpuVid [21:14]
            // CpuDfsId [13:8]
            // CpuFid [7:0] zen1..4
            // CpuFid [11:0] zen5
            // Ring0.ReadMsr(MSR_PSTATE_0 + curPstateStaus, out eax, out uint edx);
            // uint curPstate = eax;
            // int PstateEn = (int)(edx >> 31);

            ThreadAffinity.Set(previousAffinity);

            // Update clock counter and cffective clock calculation
            Threads.ForEach(t => t.UpdateMeasurements());
            EffectiveClock = Threads.Average(x => x.EffectiveClock);
            _clockEffective.Value = (float)EffectiveClock;

            if (thread.HasValidCounters())
            {
                double coreClock = 0;
                double busClock = 100.0; //bus speed in MHz
                _busSpeed ??= _cpu.Sensors.FirstOrDefault(x => x.Name == "Bus Speed");
                if (_busSpeed?.Value.HasValue == true && _busSpeed.Value > 0)
                    busClock = (double)_busSpeed.Value;

                if (thread.Cpu.Family == 0x1A)
                {
                    // zen5 (0x1A)
                    // 57896-B0-PUB_3.00.pdf, CoreCOF
                    // CoreCOF is Core current operating frequency in MHz.CoreCOF = Core::X86::Msr::PStateDef[CpuFid[11:0]] * 5MHz
                    // CpuFid[11:0]: core frequency ID.Read - write.Reset: XXXh.Specifies the core frequency multiplier.The core
                    // COF is a function of CpuFid and CpuDid, and defined by CoreCOF.
                    int curCpuFid = (int)(msrPstate & 0xfff);
                    coreClock = curCpuFid * 5;

                    // multiplier, clock speed with 100Mhz as Multiplier Reference
                    _multiplier.Value = (float)((curCpuFid * 5) / busClock);
                }
                else
                {
                    // clock zen 0x17 and 0x19
                    // 55570-B1-3.16_PUB_NRV.pdf, CoreCOF
                    // CoreCOF is (Core::X86::Msr::PStateDef[CpuFid[7:0]] / Core::X86::Msr::PStateDef[CpuDfsId]) * 200
                    // CpuFid[7:0]: core frequency ID.Read - write.Reset: XXh.Specifies the core frequency multiplier.The core
                    // COF is a function of CpuFid and CpuDid, and defined by CoreCOF.
                    int curCpuDfsId = (int)((msrPstate >> 8) & 0x3f);
                    int curCpuFid = (int)(msrPstate & 0xff);

                    coreClock = (curCpuFid / (double)curCpuDfsId * (busClock * 2));

                    // multiplier
                    _multiplier.Value = (float)(curCpuFid / (double)curCpuDfsId * 2.0);
                }

                //clock values valid when AperfDelta < MperfDelta (ratio is < 1.0)
                if (thread.AperfDelta < thread.MperfDelta)
                    coreClock = ((double)thread.AperfDelta / (double)thread.MperfDelta) * coreClock;

                CoreClock = Math.Round(coreClock);
                _clock.Value = (float)CoreClock;
            }

            // Vcore voltage
            const double vidStep = 0.00625;
            double vcc = 1.550 - (vidStep * curCpuVid);
            _vcore.Value = (float)vcc;

            // core power consumption
            //current delta time
            TimeSpan deltaTime = sampleTime - _lastSampleTime;
            if (_lastSampleTime.Ticks == 0)
            {
                deltaTime = new(0);
                _lastSampleTime = sampleTime;
                _lastPwrValue = totalEnergy;
            }
            _lastSampleTime = sampleTime;

            if (deltaTime.Ticks > 0)
            {
                // power.Value = (float) ((double)pu * 0.125);
                // energyBaseUnit = micro Joule per increment, from [ESU]
                // ticks diff
                long pwr;
                if (_lastPwrValue <= totalEnergy)
                    pwr = totalEnergy - _lastPwrValue;
                else
                    pwr = (0xffffffff - _lastPwrValue) + totalEnergy;

                // update for next sample
                _lastPwrValue = totalEnergy;

                double energy = energyBaseUnit * pwr;
                energy /= deltaTime.TotalSeconds;

                if (!double.IsNaN(energy))
                    _power.Value = (float)energy;
            }
        }
    }

    // ReSharper disable InconsistentNaming
    private const uint COFVID_STATUS = 0xC0010071;
    private const uint F17H_M01H_SVI = 0x0005A000;
    private const uint F17H_M01H_THM_TCON_CUR_TMP = 0x00059800;
    private const uint F17H_M70H_CCD1_TEMP = 0x00059954;
    private const uint F17H_M61H_CCD1_TEMP = 0x00059b08;
    private const uint F17H_TEMP_OFFSET_FLAG = 0x80000;
    private const uint FAMILY_17H_PCI_CONTROL_REGISTER = 0x60;
    private const uint HWCR = 0xC0010015;
    private const uint MSR_CORE_ENERGY_STAT = 0xC001029A;
    private const uint MSR_HARDWARE_PSTATE_STATUS = 0xC0010293;
    private const uint MSR_PKG_ENERGY_STAT = 0xC001029B;
    private const uint MSR_PSTATE_STATUS = 0xC0010063;
    private const uint MSR_PSTATE_0 = 0xC0010064;
    private const uint MSR_PWR_UNIT = 0xC0010299;
    private const uint MSR_MPERF_RO = 0xC000_00E7;
    private const uint MSR_APERF_RO = 0xC000_00E8;
    private const uint PERF_CTL_0 = 0xC0010000;
    private const uint PERF_CTR_0 = 0xC0010004;
    // ReSharper restore InconsistentNaming
}
