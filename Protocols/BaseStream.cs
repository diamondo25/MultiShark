using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MultiShark.Protocols
{
    interface IBaseStream<out TPacket, out TOpcode>
        where TPacket : IBasePacket<TOpcode>
        where TOpcode : IBaseOpcode
    {
        void Append(byte[] pBuffer);
        void Append(byte[] pBuffer, int pStart, int pLength);
        TPacket Read(DateTime pTransmitted);
    }
    abstract class BaseStream<TPacket, TOpcode> : IBaseStream<TPacket, TOpcode>
        where TPacket : IBasePacket<TOpcode>
        where TOpcode : IBaseOpcode
    {
        private const int DEFAULT_SIZE = 4096;
        protected byte[] _buffer = new byte[DEFAULT_SIZE];
        protected int _cursor = 0;


        public void Append(byte[] pBuffer) { Append(pBuffer, 0, pBuffer.Length); }
        public void Append(byte[] pBuffer, int pStart, int pLength)
        {
            if (_buffer.Length - _cursor < pLength)
            {
                int newSize = _buffer.Length * 2;
                while (newSize < _cursor + pLength) newSize *= 2;
                Array.Resize<byte>(ref _buffer, newSize);
            }
            Buffer.BlockCopy(pBuffer, pStart, _buffer, _cursor, pLength);
            _cursor += pLength;
        }

        public abstract TPacket Read(DateTime pTransmitted);
    }
}
