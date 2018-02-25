using System;
using System.Text;

namespace FastNetLib.Utils
{
    public class NetDataReaderException : ArgumentException
    {
        public NetDataReaderException() { }

        public NetDataReaderException(string message) : base(message) { }

        public NetDataReaderException(string message, Exception innerException) : base(message, innerException) { }
    }

    public class NetDataReader
    {
        private NetBuffer _buffer;

        public int Length { get { return _buffer.Length; } }

        public int Position
        {
            get { return _buffer.Tell; }
        }

        public bool EndOfData
        {
            get { return _buffer.Tell == _buffer.Length; }
        }

        public int AvailableBytes
        {
            get { return _buffer.AvailableBytes; }
        }

        public void SetSource(NetDataWriter dataWriter)
        {
            Clear();
            //_data = dataWriter.Data;
            //_position = 0;
            //_dataSize = dataWriter.Length;
            //_packet = null;
        }

        public void SetSource(NetBuffer buffer)
        {
            Clear();
            _buffer = buffer;
            _buffer.DontRecycleNow = true;
        }

        //internal void SetSource(byte[] source)
        //{
        //    Clear();
        //    _buffer.Put(source, 0, source.Length);
        //}

        //internal void SetSource(byte[] source, int offset)
        //{
        //    Clear();
        //    _buffer.Put(source, 0, source.Length);
        //}

        //internal void SetSource(byte[] source, int offset, int maxSize)
        //{
        //    Clear();
        //    _buffer.Put(source, 0, source.Length);
        //}

        //internal void SetSource(NetPacket packet)
        //{
        //    Clear();
        //    _buffer.DontRecycleNow = false;
        //    _buffer.Recycle();
        //}

        /// <summary>
        /// Clone NetDataReader without data copy (usable for OnReceive)
        /// </summary>
        /// <returns>new NetDataReader instance</returns>
        //public NetDataReader Clone()
        //{
        //    return new NetDataReader(_data, _position, _dataSize);
        //}

        public NetDataReader()
        {

        }

        //public NetDataReader(byte[] source)
        //{
        //    SetSource(source);
        //}

        //public NetDataReader(byte[] source, int offset)
        //{
        //    SetSource(source, offset);
        //}

        //public NetDataReader(byte[] source, int offset, int maxSize)
        //{
        //    SetSource(source, offset, maxSize);
        //}

        internal NetDataReader(NetBuffer buffer)
        {
            SetSource(buffer);
        }

        #region GetMethods
        public NetEndPoint GetNetEndPoint()
        {
            string host = GetString(1000);
            int port = GetInt();
            return new NetEndPoint(host, port);
        }

        public byte GetByte()
        {
            if (_buffer.ValidateRead(1) == false)
                throw new NetDataReaderException("End of buffer");
            byte res = _buffer.ReadData[_buffer.ReadPosition];
            _buffer.ReadPosition += 1;
            return res;
        }

        public sbyte GetSByte()
        {
            if (_buffer.ValidateRead(1) == false)
                throw new NetDataReaderException("End of buffer");
            var b = (sbyte)_buffer.ReadData[_buffer.ReadPosition];
            _buffer.ReadPosition++;
            return b;
        }

        public bool[] GetBoolArray()
        {
            if (_buffer.ValidateRead(2) == false)
                throw new NetDataReaderException("End of buffer");
            ushort size = BitConverter.ToUInt16(_buffer.ReadData, _buffer.ReadPosition);
            _buffer.ReadPosition += 2;
            var arr = new bool[size];
            for (int i = 0; i < size; i++)
            {
                arr[i] = GetBool();
            }
            return arr;
        }

        public ushort[] GetUShortArray()
        {
            if (_buffer.ValidateRead(2) == false)
                throw new NetDataReaderException("End of buffer");
            ushort size = BitConverter.ToUInt16(_buffer.ReadData, _buffer.ReadPosition);
            _buffer.ReadPosition += 2;
            var arr = new ushort[size];
            for (int i = 0; i < size; i++)
            {
                arr[i] = GetUShort();
            }
            return arr;
        }

        public short[] GetShortArray()
        {
            if (_buffer.ValidateRead(2) == false)
                throw new NetDataReaderException("End of buffer");
            ushort size = BitConverter.ToUInt16(_buffer.ReadData, _buffer.ReadPosition);
            _buffer.ReadPosition += 2;
            var arr = new short[size];
            for (int i = 0; i < size; i++)
            {
                arr[i] = GetShort();
            }
            return arr;
        }

        public long[] GetLongArray()
        {
            if (_buffer.ValidateRead(2) == false)
                throw new NetDataReaderException("End of buffer");
            ushort size = BitConverter.ToUInt16(_buffer.ReadData, _buffer.ReadPosition);
            _buffer.ReadPosition += 2;
            var arr = new long[size];
            for (int i = 0; i < size; i++)
            {
                arr[i] = GetLong();
            }
            return arr;
        }

        public ulong[] GetULongArray()
        {
            if (_buffer.ValidateRead(2) == false)
                throw new NetDataReaderException("End of buffer");
            ushort size = BitConverter.ToUInt16(_buffer.ReadData, _buffer.ReadPosition);
            _buffer.ReadPosition += 2;
            var arr = new ulong[size];
            for (int i = 0; i < size; i++)
            {
                arr[i] = GetULong();
            }
            return arr;
        }

        public int[] GetIntArray()
        {
            if (_buffer.ValidateRead(2) == false)
                throw new NetDataReaderException("End of buffer");
            ushort size = BitConverter.ToUInt16(_buffer.ReadData, _buffer.ReadPosition);
            _buffer.ReadPosition += 2;
            var arr = new int[size];
            for (int i = 0; i < size; i++)
            {
                arr[i] = GetInt();
            }
            return arr;
        }

        public uint[] GetUIntArray()
        {
            if (_buffer.ValidateRead(2) == false)
                throw new NetDataReaderException("End of buffer");
            ushort size = BitConverter.ToUInt16(_buffer.ReadData, _buffer.ReadPosition);
            _buffer.ReadPosition += 2;
            var arr = new uint[size];
            for (int i = 0; i < size; i++)
            {
                arr[i] = GetUInt();
            }
            return arr;
        }

        public float[] GetFloatArray()
        {
            if (_buffer.ValidateRead(2) == false)
                throw new NetDataReaderException("End of buffer");
            ushort size = BitConverter.ToUInt16(_buffer.ReadData, _buffer.ReadPosition);
            _buffer.ReadPosition += 2;
            var arr = new float[size];
            for (int i = 0; i < size; i++)
            {
                arr[i] = GetFloat();
            }
            return arr;
        }

        public double[] GetDoubleArray()
        {
            if (_buffer.ValidateRead(2) == false)
                throw new NetDataReaderException("End of buffer");
            ushort size = BitConverter.ToUInt16(_buffer.ReadData, _buffer.ReadPosition);
            _buffer.ReadPosition += 2;
            var arr = new double[size];
            for (int i = 0; i < size; i++)
            {
                arr[i] = GetDouble();
            }
            return arr;
        }

        public string[] GetStringArray()
        {
            if (_buffer.ValidateRead(2) == false)
                throw new NetDataReaderException("End of buffer");
            ushort size = BitConverter.ToUInt16(_buffer.ReadData, _buffer.ReadPosition);
            _buffer.ReadPosition += 2;
            var arr = new string[size];
            for (int i = 0; i < size; i++)
            {
                arr[i] = GetString();
            }
            return arr;
        }

        public string[] GetStringArray(int maxStringLength)
        {
            if (_buffer.ValidateRead(2) == false)
                throw new NetDataReaderException("End of buffer");
            ushort size = BitConverter.ToUInt16(_buffer.ReadData, _buffer.ReadPosition);
            _buffer.ReadPosition += 2;
            var arr = new string[size];
            for (int i = 0; i < size; i++)
            {
                arr[i] = GetString(maxStringLength);
            }
            return arr;
        }

        public bool GetBool()
        {
            if (_buffer.ValidateRead(1) == false)
                throw new NetDataReaderException("End of buffer");
            bool res = _buffer.ReadData[_buffer.ReadPosition] > 0;
            _buffer.ReadPosition += 1;
            return res;
        }

        public char GetChar()
        {
            if (_buffer.ValidateRead(1) == false)
                throw new NetDataReaderException("End of buffer");
            char result = BitConverter.ToChar(_buffer.ReadData, _buffer.ReadPosition);
            _buffer.ReadPosition += 1;
            return result;
        }

        public ushort GetUShort()
        {
            if (_buffer.ValidateRead(2) == false)
                throw new NetDataReaderException("End of buffer");
            ushort result = BitConverter.ToUInt16(_buffer.ReadData, _buffer.ReadPosition);
            _buffer.ReadPosition += 2;
            return result;
        }

        public short GetShort()
        {
            if (_buffer.ValidateRead(2) == false)
                throw new NetDataReaderException("End of buffer");
            short result = BitConverter.ToInt16(_buffer.ReadData, _buffer.ReadPosition);
            _buffer.ReadPosition += 2;
            return result;
        }

        public long GetLong()
        {
            if (_buffer.ValidateRead(8) == false)
                throw new NetDataReaderException("End of buffer");
            long result = BitConverter.ToInt64(_buffer.ReadData, _buffer.ReadPosition);
            _buffer.ReadPosition += 8;
            return result;
        }

        public ulong GetULong()
        {
            if (_buffer.ValidateRead(8) == false)
                throw new NetDataReaderException("End of buffer");
            ulong result = BitConverter.ToUInt64(_buffer.ReadData, _buffer.ReadPosition);
            _buffer.ReadPosition += 8;
            return result;
        }

        public int GetInt()
        {
            if (_buffer.ValidateRead(4) == false)
                throw new NetDataReaderException("End of buffer");
            int result = BitConverter.ToInt32(_buffer.ReadData, _buffer.ReadPosition);
            _buffer.ReadPosition += 4;
            return result;
        }

        public uint GetUInt()
        {
            if (_buffer.ValidateRead(4) == false)
                throw new NetDataReaderException("End of buffer");
            uint result = BitConverter.ToUInt32(_buffer.ReadData, _buffer.ReadPosition);
            _buffer.ReadPosition += 4;
            return result;
        }

        public float GetFloat()
        {
            if (_buffer.ValidateRead(4) == false)
                throw new NetDataReaderException("End of buffer");
            float result = BitConverter.ToSingle(_buffer.ReadData, _buffer.ReadPosition);
            _buffer.ReadPosition += 4;
            return result;
        }

        public double GetDouble()
        {
            if (_buffer.ValidateRead(8) == false)
                throw new NetDataReaderException("End of buffer");
            double result = BitConverter.ToDouble(_buffer.ReadData, _buffer.ReadPosition);
            _buffer.ReadPosition += 8;
            return result;
        }

        public string GetString(int maxLength)
        {
            int bytesCount = GetInt();
            if (bytesCount <= 0 || bytesCount > maxLength*2)
            {
                return string.Empty;
            }

            if (_buffer.ValidateRead(bytesCount) == false)
                throw new NetDataReaderException("End of buffer");
            int charCount = Encoding.UTF8.GetCharCount(_buffer.ReadData, _buffer.ReadPosition, bytesCount);
            if (charCount > maxLength)
            {
                return string.Empty;
            }

            string result = Encoding.UTF8.GetString(_buffer.ReadData, _buffer.ReadPosition, bytesCount);
            _buffer.ReadPosition += bytesCount;
            return result;
        }

        public string GetString()
        {
            int bytesCount = GetInt();
            if (bytesCount <= 0)
            {
                return string.Empty;
            }

            if (_buffer.ValidateRead(bytesCount) == false)
                throw new NetDataReaderException("End of buffer");
            string result = Encoding.UTF8.GetString(_buffer.ReadData, _buffer.ReadPosition, bytesCount);
            _buffer.ReadPosition += bytesCount;
            return result;
        }

        public byte[] GetRemainingBytes()
        {
            byte[] outgoingData = new byte[AvailableBytes];
            if(_buffer.Get(outgoingData, 0, AvailableBytes) == false)
                throw new NetDataReaderException("Buffer too small");
            return outgoingData;
        }

        public void GetRemainingBytes(byte[] destination)
        {
            if(_buffer.Get(destination, 0, AvailableBytes) == false)
                throw new NetDataReaderException("Buffer too small");
        }

        public void GetBytes(byte[] destination, int length)
        {
            if(_buffer.Get(destination, 0, length) == false)
                throw new NetDataReaderException("Buffer too small");
        }

        public byte[] GetBytesWithLength()
        {
            int length = GetInt();
            byte[] outgoingData = new byte[length];
            if(_buffer.Get(outgoingData, 0, length) == false)
                throw new NetDataReaderException("Buffer too small");
            return outgoingData;
        }
        #endregion

        #region PeekMethods

        public byte PeekByte()
        {
            if (_buffer.ValidateRead(1) == false)
                throw new NetDataReaderException("End of buffer");
            return _buffer.ReadData[_buffer.ReadPosition];
        }

        public sbyte PeekSByte()
        {
            if (_buffer.ValidateRead(1) == false)
                throw new NetDataReaderException("End of buffer");
            return (sbyte)_buffer.ReadData[_buffer.ReadPosition];
        }

        public bool PeekBool()
        {
            if (_buffer.ValidateRead(1) == false)
                throw new NetDataReaderException("End of buffer");
            return _buffer.ReadData[_buffer.ReadPosition] > 0;
        }

        public char PeekChar()
        {
            if (_buffer.ValidateRead(1) == false)
                throw new NetDataReaderException("End of buffer");
            return BitConverter.ToChar(_buffer.ReadData, _buffer.ReadPosition);
        }

        public ushort PeekUShort()
        {
            if (_buffer.ValidateRead(2) == false)
                throw new NetDataReaderException("End of buffer");
            return BitConverter.ToUInt16(_buffer.ReadData, _buffer.ReadPosition);
        }

        public short PeekShort()
        {
            if (_buffer.ValidateRead(2) == false)
                throw new NetDataReaderException("End of buffer");
            return BitConverter.ToInt16(_buffer.ReadData, _buffer.ReadPosition);
        }

        public long PeekLong()
        {
            if (_buffer.ValidateRead(8) == false)
                throw new NetDataReaderException("End of buffer");
            return BitConverter.ToInt64(_buffer.ReadData, _buffer.ReadPosition);
        }

        public ulong PeekULong()
        {
            if (_buffer.ValidateRead(8) == false)
                throw new NetDataReaderException("End of buffer");
            return BitConverter.ToUInt64(_buffer.ReadData, _buffer.ReadPosition);
        }

        public int PeekInt()
        {
            if (_buffer.ValidateRead(4) == false)
                throw new NetDataReaderException("End of buffer");
            return BitConverter.ToInt32(_buffer.ReadData, _buffer.ReadPosition);
        }

        public uint PeekUInt()
        {
            if (_buffer.ValidateRead(4) == false)
                throw new NetDataReaderException("End of buffer");
            return BitConverter.ToUInt32(_buffer.ReadData, _buffer.ReadPosition);
        }

        public float PeekFloat()
        {
            if (_buffer.ValidateRead(4) == false)
                throw new NetDataReaderException("End of buffer");
            return BitConverter.ToSingle(_buffer.ReadData, _buffer.ReadPosition);
        }

        public double PeekDouble()
        {
            if (_buffer.ValidateRead(8) == false)
                throw new NetDataReaderException("End of buffer");
            return BitConverter.ToDouble(_buffer.ReadData, _buffer.ReadPosition);
        }

        public string PeekString(int maxLength)
        {
            if (_buffer.ValidateRead(4) == false)
                throw new NetDataReaderException("End of buffer");
            int bytesCount = BitConverter.ToInt32(_buffer.ReadData, _buffer.ReadPosition);
            if (bytesCount <= 0 || bytesCount > maxLength * 2)
            {
                return string.Empty;
            }

            if (_buffer.ValidateRead(4 + bytesCount) == false)
                throw new NetDataReaderException("End of buffer");
            int charCount = Encoding.UTF8.GetCharCount(_buffer.ReadData, _buffer.ReadPosition + 4, bytesCount);
            if (charCount > maxLength)
            {
                return string.Empty;
            }

            string result = Encoding.UTF8.GetString(_buffer.ReadData, _buffer.ReadPosition + 4, bytesCount);
            return result;
        }

        public string PeekString()
        {
            if (_buffer.ValidateRead(4) == false)
                throw new NetDataReaderException("End of buffer");
            int bytesCount = BitConverter.ToInt32(_buffer.ReadData, _buffer.ReadPosition);
            if (bytesCount <= 0)
            {
                return string.Empty;
            }

            if (_buffer.ValidateRead(4 + bytesCount) == false)
                throw new NetDataReaderException("End of buffer");
            string result = Encoding.UTF8.GetString(_buffer.ReadData, _buffer.ReadPosition + 4, bytesCount);
            return result;
        }
        #endregion

        public void Clear()
        {
            if (_buffer != null)
            {
                _buffer.DontRecycleNow = false;
                _buffer.Recycle();
                _buffer = null;
            }
        }
    }
}

