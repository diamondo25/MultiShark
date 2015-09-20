using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MultiShark.Protocols.MapleStory
{
    class MapleDefinition : BaseDefinition
    {
        public ushort Version = 0;
        public byte Locale = 0;
        public ushort Opcode = 0;

        public override string ToString()
        {
            return string.Format("Locale: {0}; Version: {1}; Opcode: {2:X4}; ", Locale, Version, Opcode) + base.ToString();
        }
    }
}
