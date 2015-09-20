using MultiShark.Protocols;
using System;
using System.Text;
using System.Windows.Forms;

namespace MultiShark.Protocols
{
    interface IBasePacket<out TOpcode> where TOpcode : IBaseOpcode
    {
        string[] GetFields();
        string GetDisplayName();
        BaseDefinition ToDefinition();
        ListViewItem GetListViewItem();
        void Rewind();

        bool ReadByte(out byte pValue);
        bool ReadSByte(out sbyte pValue);
        bool ReadUShort(out ushort pValue);
        bool ReadShort(out short pValue);
        bool ReadUInt(out uint pValue);
        bool ReadInt(out int pValue);
        bool ReadFloat(out float pValue);
        bool ReadULong(out ulong pValue);
        bool ReadLong(out long pValue);
        bool ReadFlippedLong(out long pValue);
        bool ReadDouble(out double pValue);
        bool ReadBytes(byte[] pBytes);
        bool ReadBytes(byte[] pBytes, int pStart, int pLength);
        bool ReadPaddedString(out string pValue, int pLength);

        DateTime Timestamp { get; }
        bool Outbound { get; }
        string Name { get;  }
        TOpcode Opcode { get;  }

        byte[] Buffer { get; }
        int Cursor { get;  }
        int Length { get; }
        int Remaining { get; }
    }

    class BasePacket<TOpcode> : IBasePacket<TOpcode> where TOpcode : IBaseOpcode
    {
        public DateTime Timestamp { get; private set; }
        public bool Outbound { get; private set; }
        public string Name { get; private set; }
        public TOpcode Opcode { get; private set; }

        public byte[] Buffer { get; private set; }
        public int Cursor { get; private set; }
        public int Length { get { return Buffer.Length; } }
        public int Remaining { get { return Length - Cursor; } }

        internal BasePacket(DateTime pTimestamp, bool pOutbound, string pName, TOpcode opcode, byte[] pBuffer)
        {
            Timestamp = pTimestamp;
            Outbound = pOutbound;
            Buffer = pBuffer;
            Opcode = opcode;
        }

        public virtual string[] GetFields()
        {
            return new string[] {
                Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                Outbound ? "Outbound" : "Inbound",
                Buffer.Length.ToString(),
                Name 
            };
        }

        private ListViewItem _lvi = null;

        public ListViewItem GetListViewItem()
        {
            return _lvi ?? (_lvi = new ListViewItem(GetFields()) { Tag = this });
        }

        public virtual string GetDisplayName()
        {
            return Name + " (Base Packet)";
        }

        public virtual BaseDefinition ToDefinition()
        {
            return new BaseDefinition()
            {
                Outbound = Outbound,
                Name = Name,
                Ignore = false
            };
        }

        public void Rewind() { Cursor = 0; }

        public bool ReadByte(out byte pValue)
        {
            pValue = 0;
            if (Cursor + 1 > Length) return false;
            pValue = Buffer[Cursor++];
            return true;
        }
        public bool ReadSByte(out sbyte pValue)
        {
            pValue = 0;
            if (Cursor + 1 > Length) return false;
            pValue = (sbyte)Buffer[Cursor++];
            return true;
        }
        public bool ReadUShort(out ushort pValue)
        {
            pValue = 0;
            if (Cursor + 2 > Length) return false;
            pValue = (ushort)(Buffer[Cursor++] |
                              Buffer[Cursor++] << 8);
            return true;
        }
        public bool ReadShort(out short pValue)
        {
            pValue = 0;
            if (Cursor + 2 > Length) return false;
            pValue = (short)(Buffer[Cursor++] |
                             Buffer[Cursor++] << 8);
            return true;
        }
        public bool ReadUInt(out uint pValue)
        {
            pValue = 0;
            if (Cursor + 4 > Length) return false;
            pValue = (uint)(Buffer[Cursor++] |
                            Buffer[Cursor++] << 8 |
                            Buffer[Cursor++] << 16 |
                            Buffer[Cursor++] << 24);
            return true;
        }
        public bool ReadInt(out int pValue)
        {
            pValue = 0;
            if (Cursor + 4 > Length) return false;
            pValue = (int)(Buffer[Cursor++] |
                           Buffer[Cursor++] << 8 |
                           Buffer[Cursor++] << 16 |
                           Buffer[Cursor++] << 24);
            return true;
        }
        public bool ReadFloat(out float pValue)
        {
            pValue = 0;
            if (Cursor + 4 > Length) return false;
            pValue = BitConverter.ToSingle(Buffer, Cursor);
            Cursor += 4;
            return true;
        }
        public bool ReadULong(out ulong pValue)
        {
            pValue = 0;
            if (Cursor + 8 > Length) return false;
            pValue = (ulong)(Buffer[Cursor++] |
                             Buffer[Cursor++] << 8 |
                             Buffer[Cursor++] << 16 |
                             Buffer[Cursor++] << 24 |
                             Buffer[Cursor++] << 32 |
                             Buffer[Cursor++] << 40 |
                             Buffer[Cursor++] << 48 |
                             Buffer[Cursor++] << 56);
            return true;
        }
        public bool ReadLong(out long pValue)
        {
            pValue = 0;
            if (Cursor + 8 > Length) return false;
            pValue = (long)(Buffer[Cursor++] |
                            Buffer[Cursor++] << 8 |
                            Buffer[Cursor++] << 16 |
                            Buffer[Cursor++] << 24 |
                            Buffer[Cursor++] << 32 |
                            Buffer[Cursor++] << 40 |
                            Buffer[Cursor++] << 48 |
                            Buffer[Cursor++] << 56);
            return true;
        }
        public bool ReadFlippedLong(out long pValue) // 5 6 7 8 1 2 3 4
        {
            pValue = 0;
            if (Cursor + 8 > Length) return false;
            pValue = (long)(
                            Buffer[Cursor++] << 32 |
                            Buffer[Cursor++] << 40 |
                            Buffer[Cursor++] << 48 |
                            Buffer[Cursor++] << 56 |
                            Buffer[Cursor++] |
                            Buffer[Cursor++] << 8 |
                            Buffer[Cursor++] << 16 |
                            Buffer[Cursor++] << 24);
            return true;
        }
        public bool ReadDouble(out double pValue)
        {
            pValue = 0;
            if (Cursor + 8 > Length) return false;
            pValue = BitConverter.ToDouble(Buffer, Cursor);
            Cursor += 8;
            return true;
        }
        public bool ReadBytes(byte[] pBytes) { return ReadBytes(pBytes, 0, pBytes.Length); }
        public bool ReadBytes(byte[] pBytes, int pStart, int pLength)
        {
            if (Cursor + pLength > Length) return false;

            System.Buffer.BlockCopy(Buffer, Cursor, pBytes, pStart, pLength);
            Cursor += pLength;
            return true;
        }

        public bool ReadPaddedString(out string pValue, int pLength)
        {
            pValue = "";
            if (Cursor + pLength > Length) return false;
            int length = 0;
            while (length < pLength && Buffer[Cursor + length] != 0x00) ++length;
            if (length > 0) pValue = Encoding.ASCII.GetString(Buffer, Cursor, length);
            Cursor += pLength;
            return true;
        }
    }
}
