// See https://aka.ms/new-console-template for more information


using PawnIOTest;

Console.WriteLine("Hello, World!");

//PawnIOTest.Monitor monitor = new();
//monitor.RunMonitor();

VerifyPawnIO verifyPawnIO = new VerifyPawnIO();
verifyPawnIO.Step10_TestAlternativeModulesCorrectFormat();
