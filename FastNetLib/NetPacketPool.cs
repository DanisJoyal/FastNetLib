using System;
using System.Collections.Generic;
using FastNetLib.Utils;

namespace FastNetLib
{
    internal class NetPacketPool : INetPacketRecyle
    {
        private readonly FastQueue<NetPacket>[] _pool;

        public int PoolLimit = NetConstants.PoolLimit;

        public int newPacketCreated;
        public int packetPooled;
        public int packetGet;

        public bool FreePackets = false;

        // Avoid mixing big packet with small ones. Otherwise, slowly, all packets will become big by NetPacket.Realloc.
        private static int SectionDivider = 16;
        private static int[] SectionSize = { 16, 32, 64, 64, 128, 128, 128, 128 };

        public NetPacketPool()
        {
            _pool = new FastQueue<NetPacket>[9];
            _pool[0] = new FastQueue<NetPacket>();  // 16
            _pool[1] = new FastQueue<NetPacket>();  // 32
            _pool[2] = new FastQueue<NetPacket>();  // 64
            _pool[3] = _pool[2];                    // 64
            _pool[4] = new FastQueue<NetPacket>();  // 128
            _pool[5] = _pool[4];                    // 128
            _pool[6] = _pool[4];                    // 128
            _pool[7] = _pool[4];                    // 128
            _pool[8] = new FastQueue<NetPacket>();  // others
        }

        ~NetPacketPool()
        {
            FreePackets = true;
        }

        public bool Dispose(int size)
        {
            int section = (size - 1) / SectionDivider;
            if (section >= _pool.Length)
                section = _pool.Length - 1;
            return FreePackets || _pool[section].Count >= PoolLimit;
        }

        public NetPacket GetWithData(PacketProperty property, int channel, NetDataWriter writer)
        {
            var packet = Get(property, writer.Length, channel);
            Buffer.BlockCopy(writer.Data, 0, packet.RawData, 0, writer.Length);
            return packet;
        }

        public NetPacket GetWithData(PacketProperty property, int channel, byte[] data, int start, int length)
        {
            var packet = Get(property, channel, length);
            Buffer.BlockCopy(data, start, packet.RawData, 0, length);
            return packet;
        }

        private NetPacket GetPacket(int size, bool clear)
        {
            // 16, 32, 64, 128, others
            NetPacket packet = null;
            int packetSize = size;
            if (size <= NetConstants.MaxPacketSize)
            {
                int section = (size - 1) / SectionDivider;
                if (section >= _pool.Length)
                    section = _pool.Length - 1;
                else
                    packetSize = SectionSize[section];
                while (packet == null && _pool[section].Empty == false)
                {
                    packet = _pool[section].Dequeue();
                }
            }
            if (packet == null)
            {
                //allocate new packet
                packet = new NetPacket(packetSize, this);
                packet.Size = size;
                newPacketCreated++;
            }
            else
            {
                //reallocate packet data if packet not fits
                if (!packet.Realloc(size) && clear)
                {
                    //clear in not reallocated
                    Array.Clear(packet.RawData, 0, size);
                }
                packet.Size = size;
            }
            packet.DontRecycleNow = false;
            packetGet++;
            return packet;
        }

        //Get packet just for read
        public NetPacket GetAndRead(byte[] data, int start, int count)
        {
            NetPacket packet = GetPacket(count, false);
            if (!packet.FromBytes(data, start, count))
            {
                Recycle(packet);
                return null;
            }
            return packet;
        }

        //Get packet with size
        public NetPacket Get(PacketProperty property, int channel, int size)
        {
            size += NetPacket.GetHeaderSize(property);
            NetPacket packet = GetPacket(size, true);
            packet.Channel = channel;
            packet.Size = size;
            packet.Property = property;
            return packet;
        }

        public void Prepool(int nbPackets, int size)
        {
            if (size > NetConstants.MaxPacketSize)
            {
                //Dont pool big packets. Save memory
                return;
            }

            int packetSize = size;
            int section = (size - 1) / SectionDivider;
            if (section >= _pool.Length)
                section = _pool.Length - 1;
            else
                packetSize = SectionSize[section];

            for (int i = 0; i < nbPackets; ++i)
            {
                //allocate new packet
                NetPacket packet = new NetPacket(packetSize, this);
                packet.Size = size;

                if (_pool[section].Count < PoolLimit)
                {
                    packet.DontRecycleNow = true;
                    _pool[section].Enqueue(packet);
                    packetPooled++;
                }
            }
        }

        public void Recycle(NetPacket packet)
        {
            if (packet.Size > NetConstants.MaxPacketSize) 
            {
                //Dont pool big packets. Save memory
                return;
            }

            //Clean fragmented flag
            packet.IsFragmented = false;
            if(packet.DontRecycleNow == false)
            {
                int section = (packet.RawData.Length - 1) / SectionDivider;
                if (section >= _pool.Length)
                    section = _pool.Length - 1;
                if (_pool[section].Count < PoolLimit)
                {
                    packet.DontRecycleNow = true;
                    _pool[section].Enqueue(packet);
                    packetPooled++;
                }
            }
        }
    }
}
