using System.Runtime.InteropServices;

namespace libCanFestivalConsole.Sdk;

#region SDK structs
    
/// <summary>
/// CanFestival message packet. Note we set data to be a UInt64 as inside CanFestival its a fixed char[8] array
/// we cannot use fixed arrays in C# without UNSAFE so instead we just use a UInt64
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 12, Pack = 1)]
public struct Message
{
    /**< message's ID */
    public ushort cob_id;

    /**< message's ID */
    public byte rtr;

    /**< remote transmission request. (0 if not rtr message, 1 if rtr message) */
    public byte len;

    /**< message's length (0 to 8) */
    
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
    public byte[] data;
}
    
/// <summary>
/// This contains the bus name on which the can board is connected and the bit rate of the board
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct struct_s_BOARD
{

    [MarshalAs(UnmanagedType.LPStr)] 
    public string busname;

    /*The bus name on which the CAN board is connected */

    [MarshalAs(UnmanagedType.LPStr)] 
    public string baudrate; /**< The board baudrate */
};


[StructLayout(LayoutKind.Sequential)]
public struct struct_s_DEVICES
{
    public uint id;

    [MarshalAs(UnmanagedType.LPStr)]
    public string name;
};
    
#endregion

public class CanFestivalSdk
{
    #region SDK EntryPoint

    [DllImport("./Lib/libcanfestival_can_socket.so")]
    public static extern byte canReceive_driver(IntPtr handle, IntPtr msg);
    
    
    [DllImport("./Lib/libcanfestival_can_socket.so")]
    public static extern byte canSend_driver(IntPtr handle, IntPtr msg);
    
    
    [DllImport("./Lib/libcanfestival_can_socket.so")]
    public static extern IntPtr canOpen_driver(IntPtr brd);
    
    
    [DllImport("./Lib/libcanfestival_can_socket.so")]
    public static extern uint canClose_driver(IntPtr handle);

    #endregion
}