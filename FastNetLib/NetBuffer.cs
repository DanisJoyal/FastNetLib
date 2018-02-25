using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FastNetLib
{
    public class NetBuffer
    {
        private readonly List<NetPacket> _packets;
        private readonly NetPacketPool _pool;
        private readonly bool _autoResize;
        private readonly int _maxSize;
        private readonly int _mtu;
        private readonly PacketProperty _property;
        private int _channel;

        private int _totalSize;
        private int _writeSize;
        private NetPacket _writePacket;

        private int _seekPosition;
        private int _readSize;
        private int _readPacketIdx;
        private NetPacket _readPacket;

        public bool DontRecycleNow;

        // See NetPeer.CreateNetBuffer
        private NetBuffer() { }

        ~NetBuffer() { Clear(); }

        internal NetBuffer(NetPacketPool pool, PacketProperty property, int mtu, bool autoResize, int maxSize)
        {
            _pool = pool;
            _autoResize = autoResize;
            _maxSize = maxSize;
            _property = property;
            _mtu = mtu;
            _packets = new List<NetPacket>();
        }

        public int Channel
        {
            get { return _channel; }
            set { _channel = value; }
        }

        public DeliveryMethod Option
        {
            get { return NetManager.From(_property); }
        }

        internal PacketProperty Property
        {
            get { return _property; }
        }

        public int Length { get { return _totalSize; } }

        public int AvailableBytes { get { return _totalSize - _seekPosition; } }

        public void Recycle()
        {
            if (DontRecycleNow == false)
                Clear();
        }

        public void Clear()
        {
            foreach (NetPacket p in _packets)
            {
                p.DontRecycleNow = false;
                p.Recycle();
            }
            _packets.Clear();
            _readSize = _seekPosition = _totalSize = _writeSize = 0;
            _readPacket = _writePacket = null;
            _readPacketIdx = 0;
        }

        public bool Put(Array src, int srcOffset, int count)
        {
            int copyCount = 0;
            while (copyCount != count)
            {
                if (EnsureWrite(count - copyCount) == false)
                    return false;
                int toCopyCount = Math.Min(count - copyCount, _writePacket.GetDataSize() - _writeSize);
                Buffer.BlockCopy(src, copyCount, _writePacket.RawData, _writeSize, toCopyCount);
                _writeSize += toCopyCount;
                _totalSize += toCopyCount;
                copyCount += toCopyCount;
            }
            return true;
        }

        public int Tell { get { return _seekPosition; } }

        public void Seek(int position)
        {
            if (position > _totalSize)
            {
                _seekPosition = _totalSize;
                _readPacket = null;
            }
            else if (position >= 0)
            {
                _readSize = 0;
                _seekPosition = position;
                foreach(NetPacket p in _packets)
                {
                    _readPacket = p;
                    _readSize += p.GetDataSize();
                    if (_readSize > _seekPosition)
                    {
                        _readSize = _seekPosition - _readSize - p.GetDataSize();
                        break;
                    }
                }
            }
        }

        public bool Get(Array dst, int dstOffset, int count)
        {
            int totalRead = 0;
            for (; _readPacketIdx < _packets.Count; ++_readPacketIdx)
            {
                if (_readPacket == null)
                {
                    _readPacket = _packets[_readPacketIdx];
                }
                int copyCount = count - totalRead;
                if (copyCount > _readPacket.GetDataSize() - _readSize)
                    copyCount = _readPacket.GetDataSize() - _readSize;
                Buffer.BlockCopy(_readPacket.RawData, _readSize, dst, dstOffset, copyCount);
                _readSize += copyCount;
                totalRead += copyCount;
                _seekPosition += copyCount;
                if (totalRead == count)
                    break;
                _readPacket = null;
            }
            return true;
        }

        public byte[] WriteData
        {
            get { return _writePacket.RawData; }
        }

        public bool EnsureWrite(int size)
        {
            if (_writePacket == null || _writePacket.GetDataSize() > _writeSize + size)
            {
                int packetSize = _mtu - NetConstants.FragmentHeaderSize;
                if (_autoResize == false)
                {
                    if (_maxSize < _totalSize + size)
                        return false;

                    if (_maxSize < packetSize)
                        packetSize = _maxSize;
                    else if (_maxSize - _totalSize < packetSize)
                        packetSize = _maxSize - _totalSize + NetPacket.FragmentHeaderSize;
                }
                if (packetSize > 0)
                {
                    _writePacket = _pool.Get(_property, _channel, packetSize);
                    _writePacket.DontRecycleNow = true;
                    _writePacket.IsFragmented = true;
                    _writeSize = 0;
                    _packets.Add(_writePacket);
                    if (_readPacket == null)
                        _readPacket = _packets[0];
                }
            }
            return true;
        }

        public byte[] ReadData
        {
            get { return _readPacket.RawData; }
        }

        internal int ReadPosition
        {
            get { return _readSize; }
            set
            {
                if (_readSize < value)
                    _seekPosition += (value - _readSize);
                _readSize = value;
            }
        }

        internal void Read(int size)
        {
            _readSize += size;
            _seekPosition += size;
        }

        public bool ValidateRead(int size)
        {
            if(_readPacket == null || _readSize + size > _readPacket.GetDataSize())
            {
                // Get next packet
                ++_readPacketIdx;
                if (_readPacketIdx >= _packets.Count)
                    return false;
                _readPacket = _packets[_readPacketIdx];
                _readSize = 0;
            }
            return true;
        }

        internal void Receive(NetPacket p)
        {
            Clear();
            _totalSize = p.GetDataSize();
            p.DontRecycleNow = true;
            _packets.Add(p);
            _readPacket = _packets[0];
        }

        internal void Receive(NetPacket[] packets)
        {
            Clear();
            foreach(NetPacket p in packets)
            {
                _totalSize += p.GetDataSize();
                p.DontRecycleNow = true;
                _packets.Add(p);
            }
            _readPacket = _packets[0];
        }

        internal void Send(NetPeer peer)
        {
            int totalPackets = _packets.Count;
            for (ushort i = 0; i < totalPackets; i++)
            {
                NetPacket p = _packets[i];
                p.FragmentId = peer._fragmentId;
                p.FragmentPart = i;
                p.FragmentsTotal = (ushort)totalPackets;
                p.Channel = _channel;
                if (totalPackets == i - 1)
                {
                    // Last packet
                    if (_writeSize > 0)
                    {
                        if (totalPackets == 1)
                            p.IsFragmented = false;
                        p.Size = _writeSize;
                        peer.SendPacket(p);
                    }
                }
                else
                {
                    peer.SendPacket(p);
                }
            }
            peer._fragmentId++;
        }
    }
}
