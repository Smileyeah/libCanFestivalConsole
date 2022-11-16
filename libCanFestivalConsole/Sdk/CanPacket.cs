namespace libCanFestivalConsole.Sdk;

public class CanPacket
{
    public ushort Cob { get; set; }
    public byte Length { get; set; }
    
    public byte[] Data { get; set; }
    
    public bool Bridge { get; set; }

    public CanPacket()
    {
    }

    /// <summary>
    /// Construct C# CanPacket from a CanFestival messages
    /// </summary>
    /// <param name="msg">A CanFestival message struct</param>
    /// <param name="bridge"></param>
    public CanPacket(Message msg, bool bridge = false)
    {
        Cob = msg.cob_id;
        Length = msg.len;
        Data = new byte[Length];
        Bridge = bridge;

        Array.Copy(msg.data, Data, Length);
    }

    /// <summary>
    /// Convert to a CanFestival message
    /// </summary>
    /// <returns>CanFestival message</returns>
    public Message ToMessage()
    {
        var msg = new Message
        {
            cob_id = Cob,
            len = Length,
            rtr = 0
        };

        var temp = new byte[8];
        Array.Copy(Data, temp, Length);
        msg.data = temp;

        return msg;

    }

    /// <summary>
    /// Dump current packet to string
    /// </summary>
    /// <returns>Formatted string of current packet</returns>
    public override string ToString()
    {
        var output = $"{Cob:x3} {Length:x1}";

        for (var x = 0; x < Length; x++)
        {
            output += $" {Data[x]:x2}";
        }
        
        return output;
    }
}