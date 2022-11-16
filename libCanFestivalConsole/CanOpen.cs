using System.Collections.Concurrent;
using libCanFestivalConsole.Sdk;

namespace libCanFestivalConsole;

public class CanOpen
{
    public DebugLevel dbgLevel = DebugLevel.DEBUG_NONE;

    private readonly CanFestivalDriverInstance _driverInstance;

    private readonly Dictionary<ushort, CanFestivalNMTState> _nmtState = new Dictionary<ushort, CanFestivalNMTState>();

    private readonly Queue<SDOProtocol> _sdoQueue = new Queue<SDOProtocol>();

    public bool echo = true;

    public CanOpen()
    {
        _driverInstance = new CanFestivalDriverInstance();
        
        //preallocate all NMT guards
        for (byte x = 0; x < 0x80; x++)
        {
            var nmt = new CanFestivalNMTState();
            _nmtState[x] = nmt;
        }
    }

    #region driverinterface

    /// <summary>
    /// Open the CAN hardware device via the CanFestival driver, NB this is currently a simple version that will
    /// not work with drivers that have more complex bus ids so only supports com port (inc usb serial) devices for the moment
    /// </summary>
    /// <param name="comport">COM PORT number</param>
    /// <param name="speed">CAN Bit rate</param>
    public void Open(string comport, BusSpeed speed)
    {
        var opened = _driverInstance.Open($"{comport}", speed);
        if (!opened)
        {
            Console.WriteLine("Can Socket Open Failed, please confirm can-util installed");
            return;
        }

        _driverInstance.ReceiveMessageHandler -= DriverRxMessage;
        _driverInstance.ReceiveMessageHandler += DriverRxMessage;

        _threadRun = true;
        var thread = new Thread(ProcessMessageAsync);
        thread.Name = "CAN Open worker";
        thread.Start();

        ConnectionEventHandler?.Invoke(this, new ConnectionChangedEventArgs(true));
    }

    /// <summary>
    /// Is the driver open
    /// </summary>
    /// <returns>true = driver open and ready to use</returns>
    public bool IsOpen()
    {
        if (_driverInstance == null)
            return false;

        return _driverInstance.isOpen();
    }

    /// <summary>
    /// Send a Can packet on the bus
    /// </summary>
    /// <param name="p"></param>
    /// <param name="bridge"></param>
    public void SendPacket(CanPacket p, bool bridge = false)
    {
        var msg = p.ToMessage();

        _driverInstance.CanSend(msg);

        if (echo)
        {
            DriverRxMessage(msg, bridge);
        }
    }

    /// <summary>
    /// Received message callback handler
    /// </summary>
    /// <param name="msg">CanOpen message received from the bus</param>
    /// <param name="bridge"></param>
    private void DriverRxMessage(Message msg, bool bridge = false)
    {
        packetQueue.Enqueue(new CanPacket(msg, bridge));
    }


    /// <summary>
    /// Close the CanOpen CanFestival driver
    /// </summary>
    public void Close()
    {
        _threadRun = false;

        _driverInstance.Close();

        ConnectionEventHandler?.Invoke(this, new ConnectionChangedEventArgs(false));
    }

    #endregion

    readonly Dictionary<ushort, Action<byte[]>> PDOCallbacks = new Dictionary<ushort, Action<byte[]>>();
    public readonly Dictionary<ushort, SDOProtocol> SDOCallbacks = new Dictionary<ushort, SDOProtocol>();
    readonly ConcurrentQueue<CanPacket> packetQueue = new ConcurrentQueue<CanPacket>();

    public delegate void ConnectionEvent(object sender, ConnectionChangedEventArgs e);

    public event ConnectionEvent ConnectionEventHandler;

    public delegate void PacketEvent(CanPacket packet, DateTime dateTime);

    public event PacketEvent PacketEventHandler;

    public delegate void SDOEvent(CanPacket packet, DateTime dateTime);

    public event SDOEvent SDOEventHandler;

    public delegate void NMTEvent(CanPacket packet, DateTime dateTime);

    public event NMTEvent NMTEventHandler;

    public delegate void NMTECEvent(CanPacket packet, DateTime dateTime);

    public event NMTECEvent NMTECEventHandler;

    public delegate void PDOEvent(CanPacket[] packet, DateTime dateTime);

    public event PDOEvent PDOEventHandler;

    public delegate void EmergencyEvent(CanPacket packet, DateTime dateTime);

    public event EmergencyEvent EmergencyEventHandler;

    public delegate void LSSEvent(CanPacket packet, DateTime dateTime);

    public event LSSEvent lssevent;

    public delegate void TimeEvent(CanPacket packet, DateTime dateTime);

    public event TimeEvent TimeEventHandler;

    public delegate void SyncEvent(CanPacket packet, DateTime dateTime);

    public event SyncEvent SyncEventHandler;

    private bool _threadRun = true;

    /// <summary>
    /// Register a parser handler for a PDO, if a PDO is received with a matching COB this function will be called
    /// so that additional messages can be added for bus decoding and monitoring
    /// </summary>
    /// <param name="cob">COB to match</param>
    /// <param name="handler">function(byte[] data]{} function to invoke</param>
    public void RegisterPDOHandler(ushort cob, Action<byte[]> handler)
    {
        PDOCallbacks[cob] = handler;
    }

    /// <summary>
    /// Main process loop, used to get latest packets from buffer and also keep the SDOProtocol events pumped
    /// When packets are received they will be matched to any appropriate callback handlers for this specific COB type
    /// and that handler invoked.
    /// </summary>
    private void ProcessMessageAsync()
    {
        var pdoCollection = new List<CanPacket>();
        
        while (_threadRun)
        {
            if (_threadRun && packetQueue.IsEmpty && pdoCollection.Count == 0 && _sdoQueue.Count == 0 && SDOProtocol.isEmpty())
                continue;

            while (packetQueue.TryDequeue(out var canPacket))
            {
                if (!canPacket.Bridge)
                {
                    PacketEventHandler?.Invoke(canPacket, DateTime.Now);
                }

                //PDO 0x180 -- 0x57F
                if (canPacket.Cob >= 0x180 && canPacket.Cob <= 0x57F)
                {
                    if (PDOCallbacks.ContainsKey(canPacket.Cob))
                        PDOCallbacks[canPacket.Cob](canPacket.Data);

                    pdoCollection.Add(canPacket);
                }

                //SDOProtocol replies 0x601-0x67F
                if (canPacket.Cob >= 0x580 && canPacket.Cob < 0x600)
                {
                    if (canPacket.Length != 8)
                        return;

                    lock (_sdoQueue)
                    {
                        if (SDOCallbacks.ContainsKey(canPacket.Cob))
                        {
                            if (SDOCallbacks[canPacket.Cob].SDOProcess(canPacket))
                            {
                                SDOCallbacks.Remove(canPacket.Cob);
                            }
                        }
                    }

                    SDOEventHandler?.Invoke(canPacket, DateTime.Now);
                }

                if (canPacket.Cob >= 0x600 && canPacket.Cob < 0x680)
                {
                    SDOEventHandler?.Invoke(canPacket, DateTime.Now);
                }

                //NMT
                if (canPacket.Cob > 0x700 && canPacket.Cob <= 0x77f)
                {
                    var node = (byte)(canPacket.Cob & 0x07F);

                    _nmtState[node].ChangeState((NMTState)canPacket.Data[0]);
                    _nmtState[node].LastHeartbeat = DateTime.Now;

                    NMTECEventHandler?.Invoke(canPacket, DateTime.Now);
                }

                if (canPacket.Cob == 000)
                {
                    NMTEventHandler(canPacket, DateTime.Now);
                }

                if (canPacket.Cob == 0x80)
                {
                    SyncEventHandler?.Invoke(canPacket, DateTime.Now);
                }

                if (canPacket.Cob > 0x080 && canPacket.Cob <= 0xFF)
                {
                    EmergencyEventHandler?.Invoke(canPacket, DateTime.Now);
                }

                if (canPacket.Cob == 0x100)
                {
                    TimeEventHandler?.Invoke(canPacket, DateTime.Now);
                }

                if (canPacket.Cob > 0x7E4 && canPacket.Cob <= 0x7E5)
                {
                    lssevent?.Invoke(canPacket, DateTime.Now);
                }
            }

            if (pdoCollection.Count > 0)
            {
                PDOEventHandler?.Invoke(pdoCollection.ToArray(), DateTime.Now);
            }

            SDOProtocol.KickSDO();

            lock (_sdoQueue)
            {
                if (_sdoQueue.Count <= 0)
                    continue;
                
                var sdoObj = _sdoQueue.Dequeue();
                var nodeId = (ushort)(sdoObj.node + 0x580);
                
                if (SDOCallbacks.ContainsKey(nodeId))
                    continue;
                
                sdoObj.SDOCompletedEvent += (_, id) => SDOCallbacks.Remove(id);
                SDOCallbacks.Add(nodeId, sdoObj);
                sdoObj.SendSDO();
            }
        }
    }


    #region SDOHelpers

    /// <summary>
    /// Write to a node via SDOProtocol
    /// </summary>
    /// <param name="node">Node ID</param>
    /// <param name="index">Object Dictionary Index</param>
    /// <param name="subIndex">Object Dictionary sub index</param>
    /// <param name="udata">UInt32 data to send</param>
    /// <param name="completedHandler">Call back on finished/error event</param>
    /// <returns>SDOProtocol class that is used to perform the packet handshake, contains error/status codes</returns>
    public SDOProtocol SDOWrite(byte node, ushort index, byte subIndex, uint udata, Action<SDOProtocol> completedHandler)
    {
        if (index <= 0) throw new ArgumentOutOfRangeException(nameof(index));
        var bytes = BitConverter.GetBytes(udata);
        return SDOWrite(node, index, subIndex, bytes, completedHandler);
    }


    /// <summary>
    /// Write to a node via SDOProtocol
    /// </summary>
    /// <param name="node">Node ID</param>
    /// <param name="index">Object Dictionary Index</param>
    /// <param name="subIndex">Object Dictionary sub index</param>
    /// <param name="udata">Int64 data to send</param>
    /// <param name="completedHandler">Call back on finished/error event</param>
    /// <returns>SDOProtocol class that is used to perform the packet handshake, contains error/status codes</returns>
    public SDOProtocol SDOWrite(byte node, ushort index, byte subIndex, long udata, Action<SDOProtocol> completedHandler)
    {
        var bytes = BitConverter.GetBytes(udata);
        return SDOWrite(node, index, subIndex, bytes, completedHandler);
    }

    /// <summary>
    /// Write to a node via SDOProtocol
    /// </summary>
    /// <param name="node">Node ID</param>
    /// <param name="index">Object Dictionary Index</param>
    /// <param name="subIndex">Object Dictionary sub index</param>
    /// <param name="udata">UInt64 data to send</param>
    /// <param name="completedHandler">Call back on finished/error event</param>
    /// <returns>SDOProtocol class that is used to perform the packet handshake, contains error/status codes</returns>
    public SDOProtocol SDOWrite(byte node, ushort index, byte subIndex, ulong udata, Action<SDOProtocol> completedHandler)
    {
        var bytes = BitConverter.GetBytes(udata);
        return SDOWrite(node, index, subIndex, bytes, completedHandler);
    }

    /// <summary>
    /// Write to a node via SDOProtocol
    /// </summary>
    /// <param name="node">Node ID</param>
    /// <param name="index">Object Dictionary Index</param>
    /// <param name="subIndex">Object Dictionary sub index</param>
    /// <param name="udata">Int32 data to send</param>
    /// <param name="completedHandler">Call back on finished/error event</param>
    /// <returns>SDOProtocol class that is used to perform the packet handshake, contains error/status codes</returns>
    public SDOProtocol SDOWrite(byte node, ushort index, byte subIndex, int udata, Action<SDOProtocol> completedHandler)
    {
        var bytes = BitConverter.GetBytes(udata);
        return SDOWrite(node, index, subIndex, bytes, completedHandler);
    }

    /// <summary>
    /// Write to a node via SDOProtocol
    /// </summary>
    /// <param name="node">Node ID</param>
    /// <param name="index">Object Dictionary Index</param>
    /// <param name="subIndex">Object Dictionary sub index</param>
    /// <param name="udata">UInt16 data to send</param>
    /// <param name="completedHandler">Call back on finished/error event</param>
    /// <returns>SDOProtocol class that is used to perform the packet handshake, contains error/status codes</returns>
    public SDOProtocol SDOWrite(byte node, ushort index, byte subIndex, short udata, Action<SDOProtocol> completedHandler)
    {
        var bytes = BitConverter.GetBytes(udata);
        return SDOWrite(node, index, subIndex, bytes, completedHandler);
    }

    /// <summary>
    /// Write to a node via SDOProtocol
    /// </summary>
    /// <param name="node">Node ID</param>
    /// <param name="index">Object Dictionary Index</param>
    /// <param name="subIndex">Object Dictionary sub index</param>
    /// <param name="udata">UInt16 data to send</param>
    /// <param name="completedHandler">Call back on finished/error event</param>
    /// <returns>SDOProtocol class that is used to perform the packet handshake, contains error/status codes</returns>
    public SDOProtocol SDOWrite(byte node, ushort index, byte subIndex, ushort udata, Action<SDOProtocol> completedHandler)
    {
        var bytes = BitConverter.GetBytes(udata);
        return SDOWrite(node, index, subIndex, bytes, completedHandler);
    }

    /// <summary>
    /// Write to a node via SDOProtocol
    /// </summary>
    /// <param name="node">Node ID</param>
    /// <param name="index">Object Dictionary Index</param>
    /// <param name="subIndex">Object Dictionary sub index</param>
    /// <param name="udata">float data to send</param>
    /// <param name="completedHandler">Call back on finished/error event</param>
    /// <returns>SDOProtocol class that is used to perform the packet handshake, contains error/status codes</returns>
    public SDOProtocol SDOWrite(byte node, ushort index, byte subIndex, float udata, Action<SDOProtocol> completedHandler)
    {
        var bytes = BitConverter.GetBytes(udata);
        return SDOWrite(node, index, subIndex, bytes, completedHandler);
    }

    /// <summary>
    /// Write to a node via SDOProtocol
    /// </summary>
    /// <param name="node">Node ID</param>
    /// <param name="index">Object Dictionary Index</param>
    /// <param name="subIndex">Object Dictionary sub index</param>
    /// <param name="udata">a byte of data to send</param>
    /// <param name="completedHandler">Call back on finished/error event</param>
    /// <returns>SDOProtocol class that is used to perform the packet handshake, contains error/status codes</returns>
    public SDOProtocol SDOWrite(byte node, ushort index, byte subIndex, byte udata, Action<SDOProtocol> completedHandler)
    {
        var bytes = new byte[1];
        bytes[0] = udata;
        return SDOWrite(node, index, subIndex, bytes, completedHandler);
    }

    /// <summary>
    /// Write to a node via SDOProtocol
    /// </summary>
    /// <param name="node">Node ID</param>
    /// <param name="index">Object Dictionary Index</param>
    /// <param name="subIndex">Object Dictionary sub index</param>
    /// <param name="udata">a byte of unsigned data to send</param>
    /// <param name="completedHandler">Call back on finished/error event</param>
    /// <returns>SDOProtocol class that is used to perform the packet handshake, contains error/status codes</returns>
    public SDOProtocol SDOWrite(byte node, ushort index, byte subIndex, sbyte udata, Action<SDOProtocol> completedHandler)
    {
        var bytes = new byte[1];
        bytes[0] = (byte)udata;
        return SDOWrite(node, index, subIndex, bytes, completedHandler);
    }

    /// <summary>
    /// Write to a node via SDOProtocol
    /// </summary>
    /// <param name="node">Node ID</param>
    /// <param name="index">Object Dictionary Index</param>
    /// <param name="subIndex">Object Dictionary sub index</param>
    /// <param name="udata">byte[] of data (1-8 bytes) to send</param>
    /// <param name="completedHandler">Call back on finished/error event</param>
    /// <returns>SDOProtocol class that is used to perform the packet handshake, contains error/status codes</returns>
    public SDOProtocol SDOWrite(byte node, ushort index, byte subIndex, byte[] data, Action<SDOProtocol> completedHandler)
    {
        var sdo = new SDOProtocol(this._driverInstance, node, index, subIndex, SDODirection.SDO_WRITE, completedHandler, data);
        lock (_sdoQueue)
            _sdoQueue.Enqueue(sdo);
        
        return sdo;
    }

    /// <summary>
    /// Read from a remote node via SDOProtocol
    /// </summary>
    /// <param name="node">Node ID to read from</param>
    /// <param name="index">Object Dictionary Index</param>
    /// <param name="subIndex">Object Dictionary sub index</param>
    /// <param name="completedHandler">Call back on finished/error event</param>
    /// <returns>SDOProtocol class that is used to perform the packet handshake, contains returned data and error/status codes</returns>
    public SDOProtocol SDORead(byte node, ushort index, byte subIndex, Action<SDOProtocol> completedHandler)
    {
        var sdo = new SDOProtocol(this._driverInstance, node, index, subIndex, SDODirection.SDO_READ, completedHandler, null);
        lock (_sdoQueue)
            _sdoQueue.Enqueue(sdo);
        
        return sdo;
    }

    /// <summary>
    /// Get the current length of Enqueued items
    /// </summary>
    /// <returns></returns>
    public int GetSDOQueueSize()
    {
        return _sdoQueue.Count;
    }

    /// <summary>
    /// Flush the SDOProtocol queue
    /// </summary>
    public void FlushSDOQueue()
    {
        lock (_sdoQueue)
            _sdoQueue.Clear();
    }

    #endregion

    #region NMTHelpers

    public void ChangedNMTStart(byte nodeId = 0)
    {
        var p = new CanPacket();
        p.Cob = 000;
        p.Length = 2;
        p.Data = new byte[2];
        p.Data[0] = 0x01;
        p.Data[1] = nodeId;
        SendPacket(p);
    }

    public void ChangedNMTPreOp(byte nodeId = 0)
    {
        var p = new CanPacket();
        p.Cob = 000;
        p.Length = 2;
        p.Data = new byte[2];
        p.Data[0] = 0x80;
        p.Data[1] = nodeId;
        SendPacket(p);
    }

    public void ChangedNMTStop(byte nodeId = 0)
    {
        var p = new CanPacket();
        p.Cob = 000;
        p.Length = 2;
        p.Data = new byte[2];
        p.Data[0] = 0x02;
        p.Data[1] = nodeId;
        SendPacket(p);
    }

    public void ChangedNMTReset(byte nodeId = 0)
    {
        var p = new CanPacket();
        p.Cob = 000;
        p.Length = 2;
        p.Data = new byte[2];
        p.Data[0] = 0x81;
        p.Data[1] = nodeId;

        SendPacket(p);
    }

    public void ChangedNMTResetCom(byte nodeId = 0)
    {
        var p = new CanPacket();
        p.Cob = 000;
        p.Length = 2;
        p.Data = new byte[2];
        p.Data[0] = 0x82;
        p.Data[1] = nodeId;

        SendPacket(p);
    }

    public void NMT_SetStateTransitionCallback(byte node, Action<NMTState> callback)
    {
        _nmtState[node].NMTBootHandler = callback;
    }

    public bool NMT_isNodeFound(byte node)
    {
        return _nmtState[node].CurrentState != NMTState.INVALID;
    }

    public bool CheckGuard(int node, TimeSpan maxSpan)
    {
        if (DateTime.Now - _nmtState[(ushort)node].LastHeartbeat > maxSpan)
            return false;

        return true;
    }

    #endregion

    #region PDOhelpers

    public void WritePDO(ushort cob, byte[] payload)
    {
        var p = new CanPacket();
        p.Cob = cob;
        p.Length = (byte)payload.Length;
        p.Data = new byte[p.Length];
        
        for (var x = 0; x < payload.Length; x++)
            p.Data[x] = payload[x];

        SendPacket(p);
    }

    #endregion
}