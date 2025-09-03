// See https://aka.ms/new-console-template for more information


using LibreHardwareMonitor.Hardware;
using PawnIOTest;

Console.WriteLine("Hello, World!");

PawnIOTest.Monitor monitor = new();
monitor.RunMonitor();

//VerifyPawnIO verifyPawnIO = new VerifyPawnIO();
//verifyPawnIO.FullDiagnostics();
//Ring0.Open();
//PawnIoSmokeTest.Main();

//TestProgram.Main();

