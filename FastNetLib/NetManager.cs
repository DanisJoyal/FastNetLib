#if DEBUG
#define STATS_ENABLED
#endif

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Threading;
using FastNetLib.Utils;

namespace FastNetLib
{
    /// <summary>
    /// Main class for all network operations. Can be used as client and/or server.
    /// </summary>
    public sealed class NetManager
    {
        internal delegate void OnMessageReceived(byte[] data, int length, int errorCode, NetEndPoint remoteEndPoint);

        private enum NetEventType
        {
            Connect,
            Disconnect,
            Receive,
            ReceiveUnconnected,
            Error,
            ConnectionLatencyUpdated,
            DiscoveryRequest,
            DiscoveryResponse,
            ConnectionRequest
        }

        private sealed class NetEvent
        {
            public NetPeer Peer;
            public readonly NetDataReader DataReader = new NetDataReader();
            public NetEventType Type;
            public NetEndPoint RemoteEndPoint;
            public int AdditionalData;
            public DisconnectReason DisconnectReason;
            public ConnectionRequest ConnectionRequest;
            public DeliveryMethod DeliveryMethod;
            public int Channel;
        }

#if DEBUG
        private struct IncomingData
        {
            public byte[] Data;
            public NetEndPoint EndPoint;
            public DateTime TimeWhenGet;
        }
        private readonly List<IncomingData> _pingSimulationList = new List<IncomingData>(); 
        private readonly Random _randomGenerator = new Random();
        private const int MinLatencyTreshold = 5;
#endif

        private readonly NetSocket _socket;
        //private readonly Thread _logicThread;

        private readonly FastQueue<NetEvent> _netEventsQueue;
        private readonly Stack<NetEvent> _netEventsPool;
        private readonly INetEventListener _netEventListener;

        private readonly NetPeerCollection _peers;
        private readonly HashSet<NetEndPoint> _connectingPeers;
        private readonly int _maxConnections;

        internal readonly NetPacketPool NetPacketPool;

        internal long AvgUpdateTime;
        private long[] _updateTimeFilter;

        private int UpdateTime = NetConstants.DefaultUpdateTime;


        //config section
        /// <summary>
        /// Enable messages receiving without connection. (with SendUnconnectedMessage method)
        /// </summary>
        public bool UnconnectedMessagesEnabled = false;

        /// <summary>
        /// Enable nat punch messages
        /// </summary>
        public bool NatPunchEnabled = false;

        /// <summary>
        /// Interval for latency detection and checking connection
        /// </summary>
        public int PingInterval = 1000;

        /// <summary>
        /// If NetManager doesn't receive any packet from remote peer during this time then connection will be closed
        /// (including library internal keepalive packets)
        /// </summary>
        public long DisconnectTimeout = 5000;

        /// <summary>
        /// Simulate packet loss by dropping random amout of packets. (Works only in DEBUG mode)
        /// </summary>
        public bool SimulatePacketLoss = false;

        /// <summary>
        /// Simulate latency by holding packets for random time. (Works only in DEBUG mode)
        /// </summary>
        public bool SimulateLatency = false;

        /// <summary>
        /// Chance of packet loss when simulation enabled. value in percents (1 - 100).
        /// </summary>
        public int SimulationPacketLossChance = 10;

        /// <summary>
        /// Minimum simulated latency
        /// </summary>
        public int SimulationMinLatency = 30;

        /// <summary>
        /// Maximum simulated latency
        /// </summary>
        public int SimulationMaxLatency = 100;

        /// <summary>
        /// Update all the system logic before pushing events. Otherwise, its done as soon that they arrived.
        /// </summary>
        public bool PollEventsAtTheEnd = true;

        /// <summary>
        /// Allows receive DiscoveryRequests
        /// </summary>
        public bool DiscoveryEnabled = false;

        /// <summary>
        /// Merge small packets into one before sending to reduce outgoing packets count. (May increase a bit outgoing data size)
        /// </summary>
        public bool MergeEnabled = true;

        /// <summary>
        /// Delay betwen initial connection attempts
        /// </summary>
        public int ReconnectDelay = 500;

        /// <summary>
        /// Maximum connection attempts before client stops and call disconnect event.
        /// </summary>
        public int MaxConnectAttempts = 10;

        /// <summary>
        /// Index of the maximum size of MTU.
        /// -1 => Autodetect (cost some send packets to find the maximum size, be careful if there is a lot of clients)
        /// 0 => 576 - MaxUdpHeaderSize,  //Internet Path MTU for X.25 (RFC 879)
        /// 1 => 1492 - MaxUdpHeaderSize, //Ethernet with LLC and SNAP, PPPoE (RFC 1042)
        /// 2 => 1500 - MaxUdpHeaderSize, //Ethernet II (RFC 1191)
        /// 3 => 4352 - MaxUdpHeaderSize, //FDDI
        /// 4 => 4464 - MaxUdpHeaderSize, //Token ring
        /// 5 => 7981 - MaxUdpHeaderSize  //WLAN
        /// Where MaxUdpHeaderSize = 68
        /// </summary>
        public int MtuStartIdx = 1;


        /// <summary>
        /// Maximum connection attempts before client stops and call disconnect event.
        /// </summary>
        public string PasscodeKey = "";

        /// <summary>
        /// Enables socket option "ReuseAddress" for specific purposes
        /// </summary>
        public bool ReuseAddress = false;

        /// <summary>
        /// Statistics of all connections
        /// </summary>
        public readonly NetStatistics Statistics;

        //modules
        /// <summary>
        /// NatPunchModule for NAT hole punching operations
        /// </summary>
        public readonly NatPunchModule NatPunchModule;

        /// <summary>
        /// Returns true if socket listening and update thread is running
        /// </summary>
        public bool IsRunning { get; private set; }

        /// <summary>
        /// Local EndPoint (host and port)
        /// </summary>
        public int LocalPort
        {
            get { return _socket.LocalPort; }
        }

        /// <summary>
        /// Number of connections
        /// </summary>
        public int PeersCount
        {
            get { return _peers.Count; }
        }

        /// <summary>
        /// Activate Reliable Ordered channel
        /// </summary>
        public bool EnableReliableOrderedChannel = true;

        /// <summary>
        /// Activate Reliable Unordered channel
        /// </summary>
        public bool EnableReliableUnorderedChannel = false;

        /// <summary>
        /// Activate Sequenced channel
        /// </summary>
        public bool EnableSequencedChannel = true;

        /// <summary>
        /// Activate Simple channel
        /// </summary>
        public bool EnableSimpleChannel = false;

        /// <summary>
        /// Activate Simple channel
        /// </summary>
        public bool EnableIPv4 = true;

        /// <summary>
        /// Activate Simple channel
        /// </summary>
        public bool EnableIPv6 = false;

        /// <summary>
        /// NetManager constructor with maxConnections = 1 (usable for client)
        /// </summary>
        /// <param name="listener">Network events listener</param>
        public NetManager(INetEventListener listener) : this(listener, 1)
        {
            
        }

        /// <summary>
        /// NetManager constructor
        /// </summary>
        /// <param name="listener">Network events listener</param>
        /// <param name="maxConnections">Maximum connections (incoming and outcoming)</param>
        public NetManager(INetEventListener listener, int maxConnections)
        {
            //_logicThread = new Thread(UpdateLogic) { Name = "LogicThread", IsBackground = true };
            _socket = new NetSocket(ReceiveLogic);
            _netEventListener = listener;
            _netEventsQueue = new FastQueue<NetEvent>();
            _netEventsPool = new Stack<NetEvent>();
            NetPacketPool = new NetPacketPool();
            NatPunchModule = new NatPunchModule(this);
            Statistics = new NetStatistics();
            _peers = new NetPeerCollection(maxConnections);
            _connectingPeers = new HashSet<NetEndPoint>();
            _maxConnections = maxConnections;
            _updateTimeFilter = new long[3];

            // Precreate all needed Merge Packets
            NetPacketPool.PoolLimit = maxConnections * 50;
            NetPacketPool.Prepool(maxConnections * 5, MtuStartIdx >= 0 && MtuStartIdx < NetConstants.PossibleMtu.Length ? NetConstants.PossibleMtu[MtuStartIdx] : (NetConstants.MaxPacketSize - NetConstants.FragmentHeaderSize));
            NetPacketPool.Prepool(maxConnections * 30, 32);
        }

        internal void ConnectionLatencyUpdated(NetPeer fromPeer, int latency)
        {
            var evt = CreateEvent(NetEventType.ConnectionLatencyUpdated);
            evt.Peer = fromPeer;
            evt.AdditionalData = latency;
            EnqueueEvent(evt);
        }

        internal bool SendRawAndRecycle(NetPacket packet, NetEndPoint remoteEndPoint)
        {
            packet.Prepare();
            var result = SendRaw(packet.RawData, 0, packet.Size, remoteEndPoint);
            NetPacketPool.Recycle(packet);
            return result;
        }

        internal bool SendRaw(byte[] message, int start, int length, NetEndPoint remoteEndPoint)
        {
            //if (!IsRunning)
            //    return false;

            int errorCode = 0;
            if (_socket.SendTo(message, start, length, remoteEndPoint, ref errorCode) <= 0)
            {
                return false;
            }

            //10040 message to long... need to check
            //10065 no route to host
            if (errorCode == 10040)
            {
                NetUtils.DebugWrite(ConsoleColor.Red, "[SRD] 10040, datalen: {0}", length);
                return false;
            }
            if (errorCode != 0 && errorCode != 10065)
            {
                //Send error
                NetPeer fromPeer;
                if (_peers.TryGetValue(remoteEndPoint, out fromPeer))
                {
                    DisconnectPeer(fromPeer, DisconnectReason.SocketSendError, errorCode, true, null, 0, 0);
                }
                var netEvent = CreateEvent(NetEventType.Error);
                netEvent.RemoteEndPoint = remoteEndPoint;
                netEvent.AdditionalData = errorCode;
                EnqueueEvent(netEvent);
                return false;
            }
#if STATS_ENABLED
            Statistics.PacketsSent++;
            Statistics.BytesSent += (uint)length;
#endif

            return true;
        }

        internal void DisconnectPeer(
            NetPeer peer, 
            DisconnectReason reason, 
            int socketErrorCode, 
            bool force,
            byte[] data,
            int start,
            int count)
        {
            if (peer == null)
            {
                return;
            }
            ////lock (_peers)
            {
                if (!_peers.ContainsAddress(peer.EndPoint) || !peer.Shutdown(data, start, count, force))
                {
                    //invalid peer
                    return;
                }
            }

            var netEvent = CreateEvent(NetEventType.Disconnect);
            netEvent.Peer = peer;
            netEvent.AdditionalData = socketErrorCode;
            netEvent.DisconnectReason = reason;
            EnqueueEvent(netEvent);           
        }

        private void ClearPeers()
        {
            //lock (_peers)
            {
                _peers.Clear();
            }
        }

        private NetEvent CreateEvent(NetEventType type)
        {
            NetEvent evt = null;
            if (_netEventsPool.Count > 0)
            {
                evt = _netEventsPool.Pop();
            }
            if(evt == null)
            {
                evt = new NetEvent();
            }
            evt.Type = type;
            return evt;
        }

        private void EnqueueEvent(NetEvent evt)
        {
            if (PollEventsAtTheEnd == false)
            {
                ProcessEvent(evt);
            }
            else
            {
                _netEventsQueue.Enqueue(evt);
            }
        }

        private void PollEvents()
        {
            if (PollEventsAtTheEnd)
            {
                while (_netEventsQueue.Empty == false)
                {
                    ProcessEvent(_netEventsQueue.Dequeue());
                }
            }
        }


        private void ProcessEvent(NetEvent evt)
        {
            switch (evt.Type)
            {
                case NetEventType.Connect:
                    _netEventListener.OnPeerConnected(evt.Peer);
                    break;
                case NetEventType.Disconnect:
                    var info = new DisconnectInfo
                    {
                        Reason = evt.DisconnectReason,
                        AdditionalData = evt.DataReader,
                        SocketErrorCode = evt.AdditionalData
                    };
                    _netEventListener.OnPeerDisconnected(evt.Peer, info);
                    break;
                case NetEventType.Receive:
                    // Keep backward compatibility
                    _netEventListener.OnNetworkReceive(evt.Peer, evt.DataReader, evt.DeliveryMethod, evt.Channel);
                    break;
                case NetEventType.ReceiveUnconnected:
                    _netEventListener.OnNetworkReceiveUnconnected(evt.RemoteEndPoint, evt.DataReader, UnconnectedMessageType.BasicMessage);
                    break;
                case NetEventType.DiscoveryRequest:
                    _netEventListener.OnNetworkReceiveUnconnected(evt.RemoteEndPoint, evt.DataReader, UnconnectedMessageType.DiscoveryRequest);
                    break;
                case NetEventType.DiscoveryResponse:
                    _netEventListener.OnNetworkReceiveUnconnected(evt.RemoteEndPoint, evt.DataReader, UnconnectedMessageType.DiscoveryResponse);
                    break;
                case NetEventType.Error:
                    _netEventListener.OnNetworkError(evt.RemoteEndPoint, evt.AdditionalData);
                    break;
                case NetEventType.ConnectionLatencyUpdated:
                    _netEventListener.OnNetworkLatencyUpdate(evt.Peer, evt.AdditionalData);
                    break;
                case NetEventType.ConnectionRequest:
                    _netEventListener.OnConnectionRequest(evt.ConnectionRequest);
                    break;
            }

            //Recycle
            evt.DataReader.Clear();
            evt.Peer = null;
            evt.AdditionalData = 0;
            evt.RemoteEndPoint = null;
            evt.ConnectionRequest = null;
            _netEventsPool.Push(evt);
        }

        NetPacket receiveBuffer;

        //Update function
        private void UpdateLogic()
        {
            long startNowMs = NetTime.NowMs;
            long timeout = startNowMs + UpdateTime;
            if (receiveBuffer == null)
                receiveBuffer = NetPacketPool.Get(PacketProperty.Sequenced, 0, NetConstants.MaxPacketSize);
            //while (true)
            {
#if DEBUG
                if (SimulateLatency)
                {
                    var time = DateTime.UtcNow;
                    lock (_pingSimulationList)
                    {
                        for (int i = 0; i < _pingSimulationList.Count; i++)
                        {
                            var incomingData = _pingSimulationList[i];
                            if (incomingData.TimeWhenGet <= time)
                            {
                                DataReceived(incomingData.Data, incomingData.Data.Length, incomingData.EndPoint);
                                _pingSimulationList.RemoveAt(i);
                                i--;
                            }
                        }
                    }
                }
#endif

#if STATS_ENABLED
                ulong totalPacketLoss = 0;
#endif
                // Flush disconnection first
                //lock (_peers)
                {
                    for (int i = 0; i < _peers.Count; i++)
                    {
                        NetPeer netPeer = _peers[i];
                        if (netPeer.ConnectionState == ConnectionState.Disconnected)
                        {
                            _peers.RemoveAt(i);
                        }
                    }

                    //Process acks
                    for (int i = 0; i < _peers.Count; i++)
                    {
                        NetPeer netPeer = _peers[i];
                        netPeer.Update(UpdateTime);
#if STATS_ENABLED
                        totalPacketLoss += netPeer.Statistics.PacketLoss;
#endif
                    }

                    //Process ping
                    for (int i = 0; i < _peers.Count; i++)
                    {
                        _peers[i].ProcessPong(UpdateTime);
                    }
                }

#if STATS_ENABLED
                Statistics.PacketLoss = totalPacketLoss;
#endif
                _socket.Receive(false, receiveBuffer.RawData);
                _socket.Receive(true, receiveBuffer.RawData);

                PollEvents();

                long currentNowMs = NetTime.NowMs;

                int remainingTime = (int)(timeout - currentNowMs);
                Thread.Sleep(remainingTime > 0 ? remainingTime : 0);

                currentNowMs = NetTime.NowMs;
                long elapsedNowMs = currentNowMs - startNowMs;
                startNowMs = currentNowMs;

                AvgUpdateTime = (long)((elapsedNowMs * 6.0f + _updateTimeFilter[0] * 3.0f + _updateTimeFilter[1] * 2.0f + _updateTimeFilter[2] * 1.0f) / 12.0f);
                _updateTimeFilter[2] = _updateTimeFilter[1];
                _updateTimeFilter[1] = _updateTimeFilter[0];
                _updateTimeFilter[0] = elapsedNowMs;
                //break;
            }
        }
        
        private void ReceiveLogic(byte[] data, int length, int errorCode, NetEndPoint remoteEndPoint)
        {
            //Receive some info
            if (errorCode == 0)
            {
#if DEBUG
                if (SimulatePacketLoss && _randomGenerator.NextDouble() * 100 < SimulationPacketLossChance)
                {
                    //drop packet
                    return;
                }
                if (SimulateLatency)
                {
                    int latency = _randomGenerator.Next(SimulationMinLatency, SimulationMaxLatency);
                    if (latency > MinLatencyTreshold)
                    {
                        byte[] holdedData = new byte[length];
                        Buffer.BlockCopy(data, 0, holdedData, 0, length);

                        lock (_pingSimulationList)
                        {
                            _pingSimulationList.Add(new IncomingData
                            {
                                Data = holdedData,
                                EndPoint = remoteEndPoint,
                                TimeWhenGet = DateTime.UtcNow.AddMilliseconds(latency)
                            });
                        }

                        //hold packet
                        return;
                    }
                }
#endif
                try
                {
                    //ProcessEvents
                    DataReceived(data, length, remoteEndPoint);
                }
                catch(Exception e)
                {
                    //protects socket receive thread
                    NetUtils.DebugWriteError("[NM] SocketReceiveThread error: " + e );
                }
            }
            else //Error on receive
            {
                //TODO: strange?
                ClearPeers();
                var netEvent = CreateEvent(NetEventType.Error);
                netEvent.AdditionalData = errorCode;
                EnqueueEvent(netEvent);
                NetUtils.DebugWriteError("[NM] Receive error: {0}" + errorCode);
            }
        }

        private NetPeer OnConnectionSolved(ConnectionRequest request)
        {
            lock (_connectingPeers)
            {
                if (_connectingPeers.Contains(request.RemoteEndPoint))
                {
                    _connectingPeers.Remove(request.RemoteEndPoint);
                }
                else
                {
                    return null;
                }
            }
            if (request.Result == ConnectionRequestResult.Reject)
            {
                NetUtils.DebugWrite(ConsoleColor.Cyan, "[NM] Peer connect reject.");
            }
            else
            {
                //response with id
                var netPeer = new NetPeer(this, request.RemoteEndPoint, request.ConnectionId);
                NetUtils.DebugWrite(ConsoleColor.Cyan, "[NM] Received peer connection Id: {0}, EP: {1}",
                    netPeer.ConnectId, netPeer.EndPoint);

                //lock (_peers)
                {
                    //add peer to list
                    _peers.Add(request.RemoteEndPoint, netPeer);

                }
                var netEvent = CreateEvent(NetEventType.Connect);
                netEvent.Peer = netPeer;
                EnqueueEvent(netEvent);
                return netPeer;
            }
            return null;
        }

        private void DataReceived(byte[] reusableBuffer, int count, NetEndPoint remoteEndPoint)
        {
#if STATS_ENABLED
            Statistics.PacketsReceived++;
            Statistics.BytesReceived += (uint) count;
#endif

            //Try read packet
            NetPacket packet = NetPacketPool.GetAndRead(reusableBuffer, 0, count);
            if (packet == null)
            {
                NetUtils.DebugWriteError("[NM] DataReceived: bad!");
                return;
            }

            //Check unconnected
            switch (packet.Property)
            {
                case PacketProperty.DiscoveryRequest:
                    if(DiscoveryEnabled)
                    {
                        var netEvent = CreateEvent(NetEventType.DiscoveryRequest);
                        netEvent.RemoteEndPoint = remoteEndPoint;
                        netEvent.DataReader.SetSource(packet);
                        EnqueueEvent(netEvent);
                        packet.Recycle();
                    }
                    return;
                case PacketProperty.DiscoveryResponse:
                    {
                        var netEvent = CreateEvent(NetEventType.DiscoveryResponse);
                        netEvent.RemoteEndPoint = remoteEndPoint;
                        netEvent.DataReader.SetSource(packet);
                        EnqueueEvent(netEvent);
                        packet.Recycle();
                    }
                    return;
                case PacketProperty.UnconnectedMessage:
                    if (UnconnectedMessagesEnabled)
                    {
                        var netEvent = CreateEvent(NetEventType.ReceiveUnconnected);
                        netEvent.RemoteEndPoint = remoteEndPoint;
                        netEvent.DataReader.SetSource(packet);
                        EnqueueEvent(netEvent);
                        packet.Recycle();
                    }
                    return;
                case PacketProperty.NatIntroduction:
                case PacketProperty.NatIntroductionRequest:
                case PacketProperty.NatPunchMessage:
                    {
                        if (NatPunchEnabled)
                            NatPunchModule.ProcessMessage(remoteEndPoint, packet);
                        packet.Recycle();
                        return;
                    }
            }

            //Check normal packets
            NetPeer netPeer;
            //lock (_peers)
            {
                _peers.TryGetValue(remoteEndPoint, out netPeer);
            }
            if (netPeer != null && 
                netPeer.ConnectionState != ConnectionState.Disconnected)
            {
                switch (packet.Property)
                {
                    case PacketProperty.Disconnect:
                        if (netPeer.ConnectionState == ConnectionState.InProgress ||
                            netPeer.ConnectionState == ConnectionState.Connected)
                        {
                            if (BitConverter.ToInt64(packet.RawData, 0) == netPeer.ConnectId)
                            {
                                var netEvent = CreateEvent(NetEventType.Disconnect);
                                netEvent.Peer = netPeer;
                                netEvent.DataReader.SetSource(packet.RawData, sizeof(long), packet.GetDataSize() - sizeof(long));
                                netEvent.DisconnectReason = DisconnectReason.RemoteConnectionClose;
                                EnqueueEvent(netEvent);
                            }
                        }
                        break;
                    case PacketProperty.ShutdownOk:
                        if (netPeer.ConnectionState == ConnectionState.ShutdownRequested)
                        {
                            netPeer.ProcessPacket(packet);
                            NetUtils.DebugWriteForce(ConsoleColor.Cyan, "[NM] ShutdownOK!");
                        }
                        break;
                    case PacketProperty.ConnectAccept:
                        if (netPeer.ProcessConnectAccept(packet))
                        {
                            var connectEvent = CreateEvent(NetEventType.Connect);
                            connectEvent.Peer = netPeer;
                            EnqueueEvent(connectEvent);
                        }
                        break;
                    default:
                        netPeer.ProcessPacket(packet);
                        break;
                }
                packet.Recycle();
                return;
            }

            //Unacked shutdown
            if (packet.Property == PacketProperty.Disconnect)
            {
                byte[] data = { (byte)PacketProperty.ShutdownOk };
                SendRaw(data, 0, 1, remoteEndPoint);
                return;
            }

            if (packet.Property == PacketProperty.ConnectRequest && packet.Size >= 12)
            {
                lock (_connectingPeers)
                {
                    if (_connectingPeers.Contains(remoteEndPoint))
                        return;
                }
                int peersCount = GetPeersCount(ConnectionState.Connected | ConnectionState.InProgress);
                if (peersCount < _maxConnections)
                {
                    int protoId = BitConverter.ToInt32(packet.RawData, 0);
                    if (protoId != NetConstants.ProtocolId)
                    {
                        NetUtils.DebugWrite(ConsoleColor.Cyan,
                            "[NM] Peer connect reject. Invalid protocol ID: " + protoId);
                        return;
                    }

                    //Getting new id for peer
                    long connectionId = BitConverter.ToInt64(packet.RawData, sizeof(int));

                    // Read data and create request
                    var reader = new NetDataReader(null, 0, 0);
                    if (packet.GetDataSize() > sizeof(int) + sizeof(long))
                    {
                        reader.SetSource(packet.RawData, sizeof(int) + sizeof(long), packet.GetDataSize() - sizeof(int) - sizeof(long));
                    }

                    lock (_connectingPeers)
                    {
                        _connectingPeers.Add(remoteEndPoint);
                    }

                    var netEvent = CreateEvent(NetEventType.ConnectionRequest);
                    netEvent.ConnectionRequest =
                        new ConnectionRequest(connectionId, remoteEndPoint, reader, OnConnectionSolved);
                    if (String.IsNullOrEmpty(PasscodeKey) == true)
                    {
                        EnqueueEvent(netEvent);
                    }
                    else
                    {
                        netEvent.ConnectionRequest.AcceptIfKey(PasscodeKey);
                        OnConnectionSolved(netEvent.ConnectionRequest);
                    }
                }
            }
        }

        internal void ReceiveFromPeer(NetPacket packet, NetPeer fromPeer)
        {
            //NetPeer fromPeer;
            ////lock (_peers)
            //{
            //    _peers.TryGetValue(remoteEndPoint, out fromPeer);
            //}
            if (fromPeer != null)
            {
                NetUtils.DebugWrite(ConsoleColor.Cyan, "[NM] Received message");
                var netEvent = CreateEvent(NetEventType.Receive);
                netEvent.Peer = fromPeer;
                netEvent.RemoteEndPoint = fromPeer.EndPoint;
                netEvent.Channel = packet.Channel;
                switch (packet.Property)
                {
                    case PacketProperty.Unreliable:
                        netEvent.DeliveryMethod = DeliveryMethod.Unreliable;
                        break;
                    case PacketProperty.ReliableUnordered:
                        netEvent.DeliveryMethod = DeliveryMethod.ReliableUnordered;
                        break;
                    case PacketProperty.ReliableOrdered:
                        netEvent.DeliveryMethod = DeliveryMethod.ReliableOrdered;
                        break;
                    case PacketProperty.Sequenced:
                        netEvent.DeliveryMethod = DeliveryMethod.Sequenced;
                        break;
                    case PacketProperty.ReliableSequenced:
                        //TODO: netEvent.DeliveryMethod = DeliveryMethod.ReliableSequenced;
                        break;
                }
                netEvent.DataReader.SetSource(packet);
                EnqueueEvent(netEvent);
            }
        }

        /// <summary>
        /// Send data to all connected peers
        /// </summary>
        /// <param name="writer">DataWriter with data</param>
        /// <param name="options">Send options (reliable, unreliable, etc.)</param>
        /// <param name="channel">Set the channel wanted. See NetConstants.MultiChannelSize</param>
        public void SendToAll(NetDataWriter writer, DeliveryMethod options, int channel = 0)
        {
            SendToAll(writer.Data, 0, writer.Length, options, channel);
        }

        /// <summary>
        /// Send data to all connected peers
        /// </summary>
        /// <param name="data">Data</param>
        /// <param name="options">Send options (reliable, unreliable, etc.)</param>
        /// <param name="channel">Set the channel wanted. See NetConstants.MultiChannelSize</param>
        public void SendToAll(byte[] data, DeliveryMethod options, int channel = 0)
        {
            SendToAll(data, 0, data.Length, options, channel);
        }

        /// <summary>
        /// Send data to all connected peers
        /// </summary>
        /// <param name="data">Data</param>
        /// <param name="start">Start of data</param>
        /// <param name="length">Length of data</param>
        /// <param name="options">Send options (reliable, unreliable, etc.)</param>
        /// <param name="channel">Set the channel wanted. See NetConstants.MultiChannelSize</param>
        public void SendToAll(byte[] data, int start, int length, DeliveryMethod options, int channel = 0)
        {
            //lock (_peers)
            {
                for (int i = 0; i < _peers.Count; i++)
                {
                    _peers[i].Send(data, start, length, options);
                }
            }
        }

        /// <summary>
        /// Send data to all connected peers
        /// </summary>
        /// <param name="writer">DataWriter with data</param>
        /// <param name="options">Send options (reliable, unreliable, etc.)</param>
        /// <param name="excludePeer">Excluded peer</param>
        /// <param name="channel">Set the channel wanted. See NetConstants.MultiChannelSize</param>
        public void SendToAll(NetDataWriter writer, DeliveryMethod options, NetPeer excludePeer, int channel = 0)
        {
            SendToAll(writer.Data, 0, writer.Length, options, excludePeer, channel);
        }

        /// <summary>
        /// Send data to all connected peers
        /// </summary>
        /// <param name="data">Data</param>
        /// <param name="options">Send options (reliable, unreliable, etc.)</param>
        /// <param name="excludePeer">Excluded peer</param>
        /// <param name="channel">Set the channel wanted. See NetConstants.MultiChannelSize</param>
        public void SendToAll(byte[] data, DeliveryMethod options, NetPeer excludePeer, int channel = 0)
        {
            SendToAll(data, 0, data.Length, options, excludePeer, channel);
        }

        /// <summary>
        /// Send data to all connected peers
        /// </summary>
        /// <param name="data">Data</param>
        /// <param name="start">Start of data</param>
        /// <param name="length">Length of data</param>
        /// <param name="options">Send options (reliable, unreliable, etc.)</param>
        /// <param name="excludePeer">Excluded peer</param>
        /// <param name="channel">Set the channel wanted. See NetConstants.MultiChannelSize</param>
        public void SendToAll(byte[] data, int start, int length, DeliveryMethod options, NetPeer excludePeer, int channel = 0)
        {
            //lock (_peers)
            {
                for (int i = 0; i < _peers.Count; i++)
                {
                    var netPeer = _peers[i];
                    if (netPeer != excludePeer)
                    {
                        netPeer.Send(data, start, length, options);
                    }
                }
            }
        }

        /// <summary>
        /// Start logic thread and listening on available port
        /// </summary>
        public bool Start()
        {
            return Start(0);
        }

        /// <summary>
        /// Start logic thread and listening on selected port
        /// </summary>
        /// <param name="addressIPv4">bind to specific ipv4 address</param>
        /// <param name="addressIPv6">bind to specific ipv6 address</param>
        /// <param name="port">port to listen</param>
        public bool Start(string addressIPv4, string addressIPv6, int port)
        {
            if (IsRunning)
            {
                return false;
            }
            _netEventsQueue.Clear();
            IPAddress ipv4 = NetEndPoint.GetFromString(addressIPv4);
            IPAddress ipv6 = NetEndPoint.GetFromString(addressIPv6);
            if (!_socket.Bind(ipv4, ipv6, port, ReuseAddress, EnableIPv4, EnableIPv6))
                return false;
            IsRunning = true;
            //_logicThread.Start();
            return true;
        }

        /// <summary>
        /// Start logic thread and listening on selected port
        /// </summary>
        /// <param name="port">port to listen</param>
        public bool Start(int port)
        {
            if (IsRunning)
            {
                return false;
            }
            _netEventsQueue.Clear();
            if (!_socket.Bind(IPAddress.Any, IPAddress.IPv6Any, port, ReuseAddress, EnableIPv4, EnableIPv6))
                return false;
            IsRunning = true;
            //_logicThread.Start();
            return true;
        }

        /// <summary>
        /// Send message without connection
        /// </summary>
        /// <param name="message">Raw data</param>
        /// <param name="remoteEndPoint">Packet destination</param>
        /// <returns>Operation result</returns>
        public bool SendUnconnectedMessage(byte[] message, NetEndPoint remoteEndPoint)
        {
            return SendUnconnectedMessage(message, 0, message.Length, remoteEndPoint);
        }

        /// <summary>
        /// Send message without connection
        /// </summary>
        /// <param name="writer">Data serializer</param>
        /// <param name="remoteEndPoint">Packet destination</param>
        /// <returns>Operation result</returns>
        public bool SendUnconnectedMessage(NetDataWriter writer, NetEndPoint remoteEndPoint)
        {
            return SendUnconnectedMessage(writer.Data, 0, writer.Length, remoteEndPoint);
        }

        /// <summary>
        /// Send message without connection
        /// </summary>
        /// <param name="message">Raw data</param>
        /// <param name="start">data start</param>
        /// <param name="length">data length</param>
        /// <param name="remoteEndPoint">Packet destination</param>
        /// <returns>Operation result</returns>
        public bool SendUnconnectedMessage(byte[] message, int start, int length, NetEndPoint remoteEndPoint)
        {
            if (!IsRunning)
                return false;
            var packet = NetPacketPool.GetWithData(PacketProperty.UnconnectedMessage, 0, message, start, length);
            bool result = SendRawAndRecycle(packet, remoteEndPoint);
            return result;
        }

        public bool SendDiscoveryRequest(NetDataWriter writer, int port)
        {
            return SendDiscoveryRequest(writer.Data, 0, writer.Length, port);
        }

        public bool SendDiscoveryRequest(byte[] data, int port)
        {
            return SendDiscoveryRequest(data, 0, data.Length, port);
        }

        public bool SendDiscoveryRequest(byte[] data, int start, int length, int port)
        {
            if (!IsRunning)
                return false;
            var packet = NetPacketPool.GetWithData(PacketProperty.DiscoveryRequest, 0, data, start, length);
            bool result = _socket.SendBroadcast(packet.RawData, 0, packet.Size, port);
            packet.Recycle();
            return result;
        }

        public bool SendDiscoveryResponse(NetDataWriter writer, NetEndPoint remoteEndPoint)
        {
            return SendDiscoveryResponse(writer.Data, 0, writer.Length, remoteEndPoint);
        }

        public bool SendDiscoveryResponse(byte[] data, NetEndPoint remoteEndPoint)
        {
            return SendDiscoveryResponse(data, 0, data.Length, remoteEndPoint);
        }

        public bool SendDiscoveryResponse(byte[] data, int start, int length, NetEndPoint remoteEndPoint)
        {
            if (!IsRunning)
                return false;
            var packet = NetPacketPool.GetWithData(PacketProperty.DiscoveryResponse, 0, data, start, length);
            bool result = SendRawAndRecycle(packet, remoteEndPoint);
            return result;
        }

        /// <summary>
        /// Flush all queued packets of all peers
        /// </summary>
        public void Flush()
        {
            //lock (_peers)
            {
                for (int i = 0; i < _peers.Count; i++)
                {
                    _peers[i].Flush();
                }
            }
        }

        /// <summary>
        /// Receive / Send and PollEvents. Game loop needs to call this.
        /// </summary>
        public void Run(int timeout)
        {
            UpdateTime = timeout;
            UpdateLogic();
        }

        /// <summary>
        /// Connect to remote host
        /// </summary>
        /// <param name="address">Server IP or hostname</param>
        /// <param name="port">Server Port</param>
        /// <param name="key">Connection key</param>
        /// <returns>Null if connections limit reached, New NetPeer if new connection, Old NetPeer if already connected</returns>
        /// <exception cref="InvalidOperationException">Manager is not running. Call <see cref="Start()"/></exception>
        public NetPeer Connect(string address, int port, string key)
        {
            var ep = new NetEndPoint(address, port);
            return Connect(ep, key);
        }

        /// <summary>
        /// Connect to remote host
        /// </summary>
        /// <param name="address">Server IP or hostname</param>
        /// <param name="port">Server Port</param>
        /// <param name="connectionData">Additional data for remote peer</param>
        /// <returns>Null if connections limit reached, New NetPeer if new connection, Old NetPeer if already connected</returns>
        /// <exception cref="InvalidOperationException">Manager is not running. Call <see cref="Start()"/></exception>
        public NetPeer Connect(string address, int port, NetDataWriter connectionData)
        {
            var ep = new NetEndPoint(address, port);
            return Connect(ep, connectionData);
        }

        /// <summary>
        /// Connect to remote host
        /// </summary>
        /// <param name="target">Server end point (ip and port)</param>
        /// <param name="key">Connection key</param>
        /// <returns>Null if connections limit reached, New NetPeer if new connection, Old NetPeer if already connected</returns>
        /// <exception cref="InvalidOperationException">Manager is not running. Call <see cref="Start()"/></exception>
        public NetPeer Connect(NetEndPoint target, string key)
        {
            return Connect(target, NetDataWriter.FromString(key));
        }

        /// <summary>
        /// Connect to remote host
        /// </summary>
        /// <param name="target">Server end point (ip and port)</param>
        /// <param name="connectionData">Additional data for remote peer</param>
        /// <returns>Null if connections limit reached, New NetPeer if new connection, Old NetPeer if already connected</returns>
        /// <exception cref="InvalidOperationException">Manager is not running. Call <see cref="Start()"/></exception>
        public NetPeer Connect(NetEndPoint target, NetDataWriter connectionData)
        {
            if (!IsRunning)
            {
                throw new InvalidOperationException("Client is not running");
            }
            //lock (_peers)
            {
                NetPeer peer;
                if (_peers.Count >= _maxConnections)
                {
                    return null;
                }
                if (_peers.TryGetValue(target, out peer))
                {
                    //Already connected
                    return peer;
                }

                //Create reliable connection
                //And send connection request
                peer = new NetPeer(this, target, connectionData);
                _peers.Add(target, peer);
                return peer;
            }
        }

        /// <summary>
        /// Force closes connection and stop all threads.
        /// </summary>
        public void Stop()
        {
            if (!IsRunning)
                return;
            IsRunning = false;

            //Send disconnects
            //lock (_peers)
            {
                for (int i = 0; i < _peers.Count; i++)
                {
                    _peers[i].Shutdown(null, 0, 0, true);
                }
            }

            //Clear
            ClearPeers();

            //Stop
            //if (Thread.CurrentThread != _logicThread)
            //{
            //    _logicThread.Join();
            //}
            _socket.Close();
        }

        /// <summary>
        /// Get first peer. Usefull for Client mode
        /// </summary>
        /// <returns></returns>
        public NetPeer GetFirstPeer()
        {
            //lock (_peers)
            {
                if (_peers.Count > 0)
                {
                    return _peers[0];
                }
            }
            return null;
        }

        public int GetPeersCount(ConnectionState peerState)
        {
            int count = 0;
            //lock (_peers)
            {
                for (int i = 0; i < _peers.Count; i++)
                {
                    if ((_peers[i].ConnectionState & peerState) != 0)
                    {
                        count++;
                    }
                }
            }
            return count;
        }

        /// <summary>
        /// Get copy of current connected peers (slow! use GetPeersNonAlloc for best performance)
        /// </summary>
        /// <returns>Array with connected peers</returns>
        [Obsolete("Use GetPeers(ConnectionState peerState)")]
        public NetPeer[] GetPeers()
        {
            return GetPeers(ConnectionState.Connected | ConnectionState.InProgress);
        }

        /// <summary>
        /// Get copy of current connected peers (slow! use GetPeersNonAlloc for best performance)
        /// </summary>
        /// <returns>Array with connected peers</returns>
        public NetPeer[] GetPeers(ConnectionState peerState)
        {
            if (peerState == ConnectionState.Any)
            {
                //lock (_peers)
                {
                    return _peers.ToArray();
                }
            }

            List<NetPeer> peersList = new List<NetPeer>();
            GetPeersNonAlloc(peersList, peerState);
            return peersList.ToArray();
        }

        /// <summary>
        /// Get copy of peers (without allocations)
        /// </summary>
        /// <param name="peers">List that will contain result</param>
        /// <param name="peerState">State of peers</param>
        public void GetPeersNonAlloc(List<NetPeer> peers, ConnectionState peerState)
        {
            peers.Clear();
            //lock (_peers)
            {
                for(int i = 0; i < _peers.Count; i++)
                {
                    if ((_peers[i].ConnectionState & peerState) != 0)
                    {
                        peers.Add(_peers[i]);
                    }
                }
            }
        }

        public void DisconnectAll()
        {
            DisconnectAll(null, 0, 0);
        }

        public void DisconnectAll(byte[] data, int start, int count)
        {
            //Send disconnect packets
            //lock (_peers)
            {
                for (int i = 0; i < _peers.Count; i++)
                {
                    DisconnectPeer(
                        _peers[i], 
                        DisconnectReason.DisconnectPeerCalled, 
                        0, 
                        false,
                        data, 
                        start, 
                        count);
                }
            }
        }

        /// <summary>
        /// Immediately disconnect peer from server without additional data
        /// </summary>
        /// <param name="peer">peer to disconnect</param>
        public void DisconnectPeerForce(NetPeer peer)
        {
            DisconnectPeer(peer, DisconnectReason.DisconnectPeerCalled, 0, true, null, 0, 0);
        }

        /// <summary>
        /// Disconnect peer from server
        /// </summary>
        /// <param name="peer">peer to disconnect</param>
        public void DisconnectPeer(NetPeer peer)
        {
            DisconnectPeer(peer, null, 0, 0);
        }

        /// <summary>
        /// Disconnect peer from server and send additional data (Size must be less or equal MTU - 8)
        /// </summary>
        /// <param name="peer">peer to disconnect</param>
        /// <param name="data">additional data</param>
        public void DisconnectPeer(NetPeer peer, byte[] data)
        {
            DisconnectPeer(peer, data, 0, data.Length);
        }

        /// <summary>
        /// Disconnect peer from server and send additional data (Size must be less or equal MTU - 8)
        /// </summary>
        /// <param name="peer">peer to disconnect</param>
        /// <param name="writer">additional data</param>
        public void DisconnectPeer(NetPeer peer, NetDataWriter writer)
        {
            DisconnectPeer(peer, writer.Data, 0, writer.Length);
        }

        /// <summary>
        /// Disconnect peer from server and send additional data (Size must be less or equal MTU - 8)
        /// </summary>
        /// <param name="peer">peer to disconnect</param>
        /// <param name="data">additional data</param>
        /// <param name="start">data start</param>
        /// <param name="count">data length</param>
        public void DisconnectPeer(NetPeer peer, byte[] data, int start, int count)
        {
            DisconnectPeer(
                peer, 
                DisconnectReason.DisconnectPeerCalled, 
                0, 
                false,
                data, 
                start, 
                count);
            }
    }
}
