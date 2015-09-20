using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace MultiShark.Protocols.MapleStory
{
    class MaplePacket : BasePacket<MapleOpcode>
    {
        public ushort Version { get; private set; }
        public byte Locale { get; private set; }
        public uint PreDecodeIV { get; private set; }
        public uint PostDecodeIV { get; private set; }


        internal MaplePacket(DateTime pTimestamp, bool pOutbound, ushort pVersion, byte pLocale, ushort pOpcode, string pName, byte[] pBuffer, uint pPreDecodeIV, uint pPostDecodeIV)
            : base(pTimestamp, pOutbound, pName, new MapleOpcode() { Value = pOpcode }, pBuffer)
        {
            Version = pVersion;
            Locale = pLocale;
            PreDecodeIV = pPreDecodeIV;
            PostDecodeIV = pPostDecodeIV;
        }

        public override string[] GetFields()
        {
            return new string[] {
                Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                Outbound ? "Outbound" : "Inbound",
                Buffer.Length.ToString(),
                "0x" + ((MapleOpcode)Opcode).Value.ToString("X4"),
                Name,
                PostDecodeIV.ToString("X8")
            };
        }

        public override BaseDefinition ToDefinition()
        {
            return new MapleDefinition()
            {
                Outbound = Outbound,
                Name = Name,
                Ignore = false,
                Locale = Locale,
                Opcode = ((MapleOpcode)Opcode).Value,
                Version = Version
            };
        }

        public override string GetDisplayName()
        {
            return string.Format("0x{0:X4}", Opcode);
        }
    }
}
