using System.Runtime.InteropServices;
using System.Text;

namespace libCanFestivalConsole.Sdk;

public class CanFestivalDriverInstance
{
    private bool _workThreadRunning = true;
    private System.Threading.Thread _workThread;

    /// <summary>
    /// CANOpen message received callback, this will be fired upon any received complete message on the bus
    /// </summary>
    /// <param name="msg">The CanOpen message</param>
    public delegate void ReceiveMessageDelegate(Message msg, bool bridge = false);

    public event ReceiveMessageDelegate ReceiveMessageHandler;

    /// <summary>
    /// 
    /// </summary>
    private IntPtr handle;
    
    /// <summary>
    /// can device IntPtr
    /// </summary>
    private IntPtr boardPtr;

    /// <summary>
    /// can device info
    /// </summary>
    private struct_s_BOARD brd;

    /// <summary>
    /// Create a new DriverInstance, this class provides a wrapper between the C# world and the C API dlls from canFestival that
    /// provide access to the CAN hardware devices. The exposed delegates represent the 5 defined entry points that all can festival
    /// drivers expose to form the common driver interface API. Usually the DriverLoader class will directly call this constructor.
    /// </summary>
    public CanFestivalDriverInstance()
    {
        handle = IntPtr.Zero;
        boardPtr = IntPtr.Zero;
    }

    /// <summary>
    /// Open the CAN device, the bus ID and bit rate are passed to driver. For Serial/USb Seral pass COMx etc.
    /// </summary>
    /// <param name="bus">The requested bus ID are provided here.</param>
    /// <param name="speed">The requested CAN bit rate</param>
    /// <returns>True on successful opening of device</returns>
    public bool Open(string bus, BusSpeed speed)
    {
        try
        {
            brd.busname = bus;

            // Map BusSpeed to CanFestival speed options
            brd.baudrate = speed switch
            {
                BusSpeed.BUS_10KB => "10K",
                BusSpeed.BUS_20KB => "20K",
                BusSpeed.BUS_50KB => "50K",
                BusSpeed.BUS_100KB => "100K",
                BusSpeed.BUS_125KB => "125K",
                BusSpeed.BUS_250KB => "250K",
                BusSpeed.BUS_500KB => "500K",
                BusSpeed.BUS_1MB => "1M",
                _ => brd.baudrate
            };

            boardPtr = Marshal.AllocHGlobal(Marshal.SizeOf(brd));
            Marshal.StructureToPtr(brd, boardPtr, false);

            handle = CanFestivalSdk.canOpen_driver(boardPtr);

            if (handle != IntPtr.Zero)
            {
                _workThread = new Thread(ReceiveThreadWorker);
                _workThread.Start();
                return true;
            }

            var errCode = Marshal.GetLastSystemError();
            throw new Exception($"Failed to load library (ErrorCode: {errCode})");
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.ToString());
            
            return false;
        }
    }

    /// <summary>
    /// See if the CAN device is open
    /// </summary>
    /// <returns>Open status of can device</returns>
    public bool isOpen()
    {
        if (handle == IntPtr.Zero)
            return false;

        return true;
    }

    /// <summary>
    /// Close the CAN hardware device
    /// </summary>
    public void Close()
    {
        _workThreadRunning = false;

        System.Threading.Thread.Sleep(100);

        if (_workThread != null)
        {
            while (_workThread.ThreadState == System.Threading.ThreadState.Running)
            {
                System.Threading.Thread.Sleep(1);
            }
        }

        if (handle != IntPtr.Zero)
            CanFestivalSdk.canClose_driver(handle);

        handle = IntPtr.Zero;

        if (boardPtr != IntPtr.Zero)
            Marshal.FreeHGlobal(boardPtr);

        boardPtr = IntPtr.Zero;
    }

    /// <summary>
    /// Message pump function. This should be called in a fast loop
    /// </summary>
    /// <returns></returns>
    public Message CanReceived()
    {
        // I think we can do better here and not allocated/deallocate to heap every pump loop
        var msg = new Message();

        var msgPtr = Marshal.AllocHGlobal(Marshal.SizeOf(msg));
        Marshal.StructureToPtr(msg, msgPtr, false);

        var status = CanFestivalSdk.canReceive_driver(handle, msgPtr);

        msg = (Message)Marshal.PtrToStructure(msgPtr, typeof(Message));

        Marshal.FreeHGlobal(msgPtr);

        return msg;

    }

    /// <summary>
    /// Send a CanOpen message to the hardware device
    /// </summary>
    /// <param name="msg">CanOpen message to be sent</param>
    public void CanSend(Message msg)
    {
        var msgPtr = Marshal.AllocHGlobal(Marshal.SizeOf(msg));
        Marshal.StructureToPtr(msg, msgPtr, false);

        if (handle != IntPtr.Zero)
            CanFestivalSdk.canSend_driver(handle, msgPtr);

        Marshal.FreeHGlobal(msgPtr);

    }

    /// <summary>
    /// Private worker thread to keep the CanReceived function pumped
    /// </summary>
    private void ReceiveThreadWorker()
    {
        try
        {
            while (_workThreadRunning)
            {
                var receivedMsg = CanReceived();

                if (receivedMsg.len != 0)
                {
                    ReceiveMessageHandler?.Invoke(receivedMsg);
                }
            }
        }
        catch(Exception ex)
        {
            Console.WriteLine(ex.ToString());
        }
    }
}