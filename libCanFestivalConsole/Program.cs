// See https://aka.ms/new-console-template for more information

using System.Diagnostics;
using libCanFestivalConsole;
using libCanFestivalConsole.Sdk;

Console.WriteLine("Can port init...");
try
{
    var initProcess = new ProcessStartInfo("./Lib/can_init.sh");
    var process = Process.Start(initProcess);

    process?.WaitForExit();
}
catch (Exception ex)
{
    Console.WriteLine($"Can port init failed! Exception: {ex}");
    return;
}

Console.WriteLine("Press c or C to start!");
while (Console.ReadKey().KeyChar != 'c' && Console.ReadKey().KeyChar != 'C')
{
    Thread.Sleep(1000);
}

Console.WriteLine("CANFestival test start!");

try
{
    var canOpen = new CanOpen();

    canOpen.ConnectionEventHandler += (sender, args) =>
    {
        Console.WriteLine($"CANFestival ConnectionEventHandler is happening! Result: {args.Connected} ");
    };
    
    canOpen.NMTEventHandler += (sender, args) =>
    {
        Console.WriteLine($"CANFestival NMTEventHandler is happening! Data: {sender}, time: {args}");
    };

    canOpen.NMTECEventHandler += (sender, args) =>
    {
        Console.WriteLine($"CANFestival NMTECEventHandler is happening! Data: {sender}, time: {args}");
    };

    canOpen.PDOEventHandler += (sender, args) =>
    {
        var data = sender.FirstOrDefault();
        Console.WriteLine($"CANFestival PDOEventHandler is happening! Data: {data?.ToString()}, time: {args}");
    };

    canOpen.SDOEventHandler += (sender, args) =>
    {
        Console.WriteLine($"CANFestival SDOEventHandler is happening! Data: {sender}, time: {args}");
    };

    canOpen.Open("0", BusSpeed.BUS_1MB);

    Console.WriteLine("Listening for any traffic");
    Console.WriteLine("Sending NMT reset all nodes in 5 seconds");
    Thread.Sleep(5000);
    
    canOpen.ChangedNMTReset();
    
    Console.WriteLine("Sending NMT start all nodes in 5 seconds");
    Thread.Sleep(5000);
    canOpen.ChangedNMTStart();
    
    Console.WriteLine("Sending SDO nodeId = 02, OD=1802-02, change report model in 5 seconds");
    Thread.Sleep(5000);
    canOpen.SDOWrite(0x02, 0x1800, 0x02, (byte)0xFE, protocol =>
    {
        
    });

    Console.WriteLine("Press any key to exit test...");
    while (!Console.KeyAvailable)
    {
        Console.WriteLine("Sending PDO nodeId = 02, value = FF, change report model in 5 seconds");
        Thread.Sleep(5000);
        canOpen.WritePDO(0x0202, new byte[] { 0xFF });
        
        Console.WriteLine("Sending PDO nodeId = 02, value = 00, change report model in 5 seconds");
        Thread.Sleep(5000);
        canOpen.WritePDO(0x0202, new byte[] { 0x00 });
    }
    
    canOpen.Close();
}
catch (Exception ex)
{
    Console.WriteLine(ex.ToString());
}