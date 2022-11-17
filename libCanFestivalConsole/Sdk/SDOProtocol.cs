using libCanFestivalConsole.Sdk;

namespace libCanFestivalConsole.Sdk;

/// <summary>
/// Direction of the SDO transfer
/// </summary>
public enum SDODirection
{
    SDO_READ = 0,
    SDO_WRITE = 1,
}

/// <summary>
/// Possible SDO states used by simple handshake state machine
/// </summary>
public enum SDOState
{
    SDO_INIT,
    SDO_SENT,
    SDO_HANDSHAKE,
    SDO_FINISHED,
    SDO_ERROR
}

public class SDOProtocol
{
    /// <summary>
    /// SDO读取完成时事件委托
    /// </summary>
    public delegate void SDOCompletedDelegate(object sender, byte nodeId);
    public event SDOCompletedDelegate SDOCompletedEvent;
    
    /// <summary>
    /// Expedited data buffer, if transfer is 4 bytes or less, its here
    /// </summary>

    public readonly byte node;

    //FIX me i was using all these from outside this class
    //should see if that is really needed or if better accessors are required
    //may be readonly access etc.
    public byte[] DataBuffer
    {
        get;
        private set;
    }
    
    public SDOState SDOState
    {
        get;
        set;
    }

    /// <summary>
    /// 字典主索引
    /// </summary>
    public ushort Index
    {
        get; 
        set;
    }
    
    /// <summary>
    /// 字典子索引
    /// </summary>
    public byte SubIndex 
    { 
        get;
        set;
    }
    
    /// <summary>
    /// 加急数据
    /// </summary>
    public uint ExpeditedData
    {
        get;
        set;
    }

    public bool Expedited
    {
        get; 
        set;
    }

    static readonly List<SDOProtocol> ActiveSDO = new List<SDOProtocol>();

    private readonly Action<SDOProtocol> _completedAction;
    private readonly SDODirection _direction;

    private uint _totalData;
    private readonly CanFestivalDriverInstance _canDriver;
    private bool _lastToggle = false;
    private DateTime _timeout;
    private readonly ManualResetEvent _finishedEvent;
    private DebugLevel dbgLevel;


    /// <summary>
    /// Construct a new SDO object
    /// </summary>
    /// <param name="canDriver">a libCanopenSimple object that will give access to the hardware</param>
    /// <param name="node">The note to talk to (UInt16)</param>
    /// <param name="index">The index in the object dictionary to access</param>
    /// <param name="subIndex">The subindex in the object dictionary to access</param>
    /// <param name="sdoDirection"></param>
    /// <param name="completedAction">Optional, completed callback (or null if not required)</param>
    /// <param name="dataBuffer">A byte array of data to be transfered to or from if more than 4 bytes</param>
    public SDOProtocol(
        CanFestivalDriverInstance canDriver,
        byte node,
        ushort index,
        byte subIndex,
        SDODirection sdoDirection,
        Action<SDOProtocol> completedAction,
        byte[] dataBuffer)
    {
        this._canDriver = canDriver;
        this.Index = index;
        this.SubIndex = subIndex;
        this.node = node;
        this._direction = sdoDirection;
        this._completedAction = completedAction;
        this.DataBuffer = dataBuffer;

        _finishedEvent = new ManualResetEvent(false);
        SDOState = SDOState.SDO_INIT;
    }

    /// <summary>
    /// Add this SDO object to the active list
    /// </summary>
    public void SendSDO()
    {
        lock (ActiveSDO)
            ActiveSDO.Add(this);
    }

    public static bool isEmpty()
    {
        return ActiveSDO.Count == 0;
    }

    /// <summary>
    /// Has the SDO transfer finished?
    /// </summary>
    /// <returns>True if the SDO has finished and fired its finished event</returns>
    public bool WaitOne()
    {
        return _finishedEvent.WaitOne();
    }


    /// <summary>
    /// SDO pump, call this often
    /// </summary>
    public static void KickSDO()
    {
        var toKill = new List<SDOProtocol>();

        lock (ActiveSDO)
        {
            foreach (var s in ActiveSDO)
            {
                s.KickSDOInternal();
                if (s.SDOState == SDOState.SDO_FINISHED || s.SDOState == SDOState.SDO_ERROR)
                {
                    toKill.Add(s);
                }
            }
        }

        foreach (var s in toKill)
        {
            lock (ActiveSDO)
                ActiveSDO.Remove(s);
            
            s.SDOFinish();
        }
    }

    /// <summary>
    /// State machine for a specific SDO instance
    /// </summary>
    private void KickSDOInternal()
    {
        if (SDOState != SDOState.SDO_INIT && DateTime.Now > _timeout)
        {
            SDOState = SDOState.SDO_ERROR;

            Console.WriteLine("SDO Timeout Error on {0:x4}/{1:x2} {2:x8}", this.Index, this.SubIndex, ExpeditedData);

            _completedAction?.Invoke(this);

            return;
        }

        if (SDOState != SDOState.SDO_INIT) return;
        
        _timeout = DateTime.Now + new TimeSpan(0, 0, 5);
        SDOState = SDOState.SDO_SENT;

        byte cmd;
        if (_direction == SDODirection.SDO_READ)
        {
            cmd = 0x40;
            var payload = new byte[4];
            SendPacket(cmd, payload);
            return;
        }

        var present = false;
        Expedited = true;

        switch (DataBuffer.Length)
        {
            case 1:
                cmd = 0x2f;
                break;
            case 2:
                cmd = 0x2b;
                break;
            case 3:
                cmd = 0x27;
                break;
            case 4:
                cmd = 0x23;
                break;
            default:
                //Bigger than 4 bytes we use segmented transfer
                cmd = 0x21;
                Expedited = false;

                var payload = new byte[4];
                payload[0] = (byte)DataBuffer.Length;
                payload[1] = (byte)(DataBuffer.Length >> 8);
                payload[2] = (byte)(DataBuffer.Length >> 16);
                payload[3] = (byte)(DataBuffer.Length >> 24);

                ExpeditedData = (uint)DataBuffer.Length;
                _totalData = 0;

                present = true;
                SendPacket(cmd, payload);
                break;
        }

        if (present == false)
            SendPacket(cmd, DataBuffer);
    }

    /// <summary>
    /// Send a SDO packet, with command and payload, should be only called from SDO state machine
    /// </summary>
    /// <param name="cmd">SDO command byte</param>
    /// <param name="payload">Data payload to send</param>
    private void SendPacket(byte cmd, byte[] payload)
    {
        var p = new CanPacket();
        p.Cob = (ushort)(0x600 + node);
        p.Length = 8;
        p.Data = new byte[8];
        p.Data[0] = cmd;
        p.Data[1] = (byte)Index;
        p.Data[2] = (byte)(Index >> 8);
        p.Data[3] = SubIndex;

        var sendLen = 4;

        if (payload.Length < 4)
            sendLen = payload.Length;

        for (var x = 0; x < sendLen; x++)
        {
            p.Data[4 + x] = payload[x];
        }

        if (dbgLevel == DebugLevel.DEBUG_ALL)
            Console.WriteLine($"Sending a new SDO packet: {p}");

        if (_canDriver.isOpen())
            _canDriver.CanSend(p.ToMessage());
    }

    /// <summary>
    /// Segmented transfer update function, should be only called from SDO state machine
    /// </summary>
    /// <param name="cmd">SDO command byte</param>
    /// <param name="payload">Data payload</param>
    private void SendPacketSegment(byte cmd, byte[] payload)
    {
        var p = new CanPacket();
        p.Cob = (ushort)(0x600 + node);
        p.Length = 8;
        p.Data = new byte[8];
        p.Data[0] = cmd;

        for (var x = 0; x < payload.Length; x++)
        {
            p.Data[1 + x] = payload[x];
        }

        if (dbgLevel == DebugLevel.DEBUG_ALL)
            Console.WriteLine($"Sending a new segmented SDO packet: {p.ToString()}");

        _canDriver.CanSend(p.ToMessage());
    }

    /// <summary>
    /// Force finish the SDO and trigger its finished event
    /// </summary>
    public void SDOFinish()
    {
        this.SDOCompletedEvent?.Invoke(this, this.node);
        _finishedEvent.Set();
    }

    /// <summary>
    /// SDO Instance processor, process current SDO reply and decide what to do next
    /// </summary>
    /// <param name="canPacket">SDO Canpacket to process</param>
    /// <returns></returns>
    public bool SDOProcess(CanPacket canPacket)
    {
        var SCS = canPacket.Data[0] >> 5; //7-5

        var n = 0x03 & (canPacket.Data[0] >> 2); //3-2 data size for normal packets

        var e = 0x01 & (canPacket.Data[0] >> 1); // expedited flag
        var s = canPacket.Data[0] & 0x01; // data size set flag

        var sn = 0x07 & (canPacket.Data[0] >> 1); //3-1 data size for segment packets
        var t = 0x01 & (canPacket.Data[0] >> 4); //toggle flag

        var c = 0x01 & canPacket.Data[0]; //More segments to upload?

        switch (SCS)
        {
            // ERROR abort
            case 0x04:
            {
                ExpeditedData = (uint)(canPacket.Data[4] + (canPacket.Data[5] << 8) + (canPacket.Data[6] << 16) + (canPacket.Data[7] << 24));
                DataBuffer = BitConverter.GetBytes(ExpeditedData);

                SDOState = SDOState.SDO_ERROR;

                Console.WriteLine("SDO Error on {0:x4}/{1:x2} {2:x8}", this.Index, this.SubIndex, ExpeditedData);

                _completedAction?.Invoke(this);

                return true;
            }
            // Write complete
            case 0x03:
            {
                var index = (ushort)(canPacket.Data[1] + (canPacket.Data[2] << 8));
                var sub = canPacket.Data[3];

                var node = canPacket.Cob - 0x580;
                lock (ActiveSDO)
                {
                    foreach (var sdo in ActiveSDO)
                    {
                        if (sdo.node != node) 
                            continue;
                
                        if (index != sdo.Index || sub != sdo.SubIndex) 
                            continue; //if segments break its here
                
                        if (Expedited)
                            continue;
                
                        SDOState = SDOState.SDO_HANDSHAKE;
                        RequestNextSegment(false);
                        return false;
                    }
                }

                SDOState = SDOState.SDO_FINISHED;
                _completedAction?.Invoke(this);
                return true;
            }
            
            // Write segment complete
            case 0x01 when _totalData < ExpeditedData:
            {
                _lastToggle = !_lastToggle;
                RequestNextSegment(_lastToggle);
                break;
            }
            
            case 0x01:
            {
                SDOState = SDOState.SDO_FINISHED;
                _completedAction?.Invoke(this);
                break;
            }
            
            //if expedited just handle the data
            case 0x02 when e == 1:
                //Expedited and length are set so its a regular short transfer
                ExpeditedData = (uint)(canPacket.Data[4] + (canPacket.Data[5] << 8) + (canPacket.Data[6] << 16) + (canPacket.Data[7] << 24));
                DataBuffer = BitConverter.GetBytes(ExpeditedData);

                SDOState = SDOState.SDO_FINISHED;
                _completedAction?.Invoke(this);

                return true;
            case 0x02:
            {
                var count = (uint)(canPacket.Data[4] + (canPacket.Data[5] << 8) + (canPacket.Data[6] << 16) + (canPacket.Data[7] << 24));

                Console.WriteLine("RX Segmented transfer start length is {0}", count);
                ExpeditedData = count;
                DataBuffer = new byte[ExpeditedData];
                _totalData = 0;
                //Request next segment

                RequestNextSegment(false); //toggle off on first request
                return false;
            }
            case 0x00:
            {
                // segmented transfer
                var segCount = (uint)(7 - sn);
                Console.WriteLine("RX Segmented transfer update length is {0} -- {1}", segCount, _totalData);

                for (var x = 0; x < segCount; x++)
                {
                    if (_totalData + x < DataBuffer.Length)
                        DataBuffer[_totalData + x] = canPacket.Data[1 + x];
                }

                _totalData += 7;
                if (_totalData < ExpeditedData && c == 0)
                {
                    _lastToggle = !_lastToggle;
                    RequestNextSegment(_lastToggle);
                }
                else
                {
                    SDOState = SDOState.SDO_FINISHED;
                    _completedAction?.Invoke(this);
                }
                
                break;
            }
        }

        return false;
    }

    /// <summary>
    /// Request the next segment in a segmented transfet
    /// </summary>
    /// <param name="toggle">Segmented transfer toggle flag, should alternate for each successive transfer</param>
    private void RequestNextSegment(bool toggle)
    {
        _timeout = DateTime.Now + new TimeSpan(0, 0, 5);

        if (_direction == SDODirection.SDO_READ)
        {
            byte cmd = 0x60;
            if (toggle)
                cmd |= 0x70;

            SendPacket(cmd, new byte[4]);
        }
        else
        {
            byte cmd = 0x00;
            if (toggle)
                cmd |= 0x10;

            var byteCount = (int)(DataBuffer.Length - _totalData); //11 - 7
            if (byteCount >= 7)
            {
                byteCount = 7;
            }

            var nextData = new byte[byteCount];

            for (var x = 0; x < byteCount; x++)
            {
                if (DataBuffer.Length > (_totalData + x))
                    nextData[x] = DataBuffer[_totalData + x];

            }

            if (_totalData + 7 >= DataBuffer.Length)
            {
                cmd |= 0x01; //END of packet sequence
            }

            if (byteCount != 7)
            {
                var n = 7 - byteCount;
                n = n << 1;
                cmd |= (byte)n;
            }

            SendPacketSegment(cmd, nextData);
            _totalData += (uint)byteCount;
        }

    }
}