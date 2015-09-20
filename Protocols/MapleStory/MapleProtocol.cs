using MapleLib.PacketLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace MultiShark.Protocols.MapleStory
{
    class MapleProtocol : BaseProtocol<MaplePacket, MapleOpcode>
    {
        private ushort _version = 0;
        private byte _locale = 0;
        private string _patchLocation = "";


        public override ColumnHeader[] GetListViewHeaders()
        {
            return new ColumnHeader[] {
                new ColumnHeader() {
                    Text = "Timestamp",
                    Width = 175
                },
                new ColumnHeader() {
                    Text = "Direction",
                    Width = 75
                },
                new ColumnHeader() {
                    Text = "Length",
                    Width = 64
                },
                new ColumnHeader() {
                    Text = "Opcode"
                },
                new ColumnHeader() {
                    Text = "Name",
                    Width = 174
                }
            };
        }

        public override BaseDefinition GetDefinition(IBasePacket<IBaseOpcode> packet)
        {
            return null;
        }

        public override string GetScriptLocation(IBasePacket<IBaseOpcode> p)
        {
            var packet = (MaplePacket)p;
            return packet.Locale.ToString() + Path.DirectorySeparatorChar + packet.Version.ToString() + Path.DirectorySeparatorChar + (packet.Outbound ? "Outbound" : "Inbound") + Path.DirectorySeparatorChar + "0x" + ((MapleOpcode)packet.Opcode).Value.ToString("X4") + ".txt";
        }

        public override string GetCommonScriptLocation()
        {
            return _locale.ToString() + Path.DirectorySeparatorChar + _version.ToString() + Path.DirectorySeparatorChar + "Common.txt";
        }

        public static KeyValuePair<MapleProtocol, MaplePacket>? ParseHandshake(byte[] data, DateTime arrivalTime)
        {
            var packet = new PacketReader(data);


            packet.ReadUShort();
            ushort version = packet.ReadUShort();
            byte subVersion = 1;
            string patchLocation = packet.ReadMapleString();
            byte[] localIV = packet.ReadBytes(4);
            byte[] remoteIV = packet.ReadBytes(4);
            byte locale = packet.ReadByte();

            bool isKMS = false;

            if (locale > 0x12)
            {
                return null;
            }

            if (locale == 0x02 || (locale == 0x01 && version > 255)) isKMS = true;

            if (isKMS)
            {
                int test = int.Parse(patchLocation);
                version = (ushort)(test & 0x7FFF);
                subVersion = (byte)((test >> 16) & 0xFF);
            }
            else if (patchLocation.All(character => { return character >= '0' && character <= '9'; }))
            {
                if (!byte.TryParse(patchLocation, out subVersion))
                    Console.WriteLine("Failed to parse subVersion");
            }

            var session = new MapleProtocol
            {
                _version = version,
                _locale = locale,
                _patchLocation = patchLocation,
                InboundStream = new MapleStream(false, version, locale, remoteIV, subVersion),
                OutboundStream = new MapleStream(true, version, locale, localIV, subVersion)
            };

            // Generate HandShake packet
            Definition definition = Config.Instance.GetDefinition(version, locale, false, 0xFFFF);
            if (definition == null)
            {
                definition = new Definition();
                definition.Outbound = false;
                definition.Locale = locale;
                definition.Opcode = 0xFFFF;
                definition.Name = "Maple Handshake";
                definition.Build = session._version;
                DefinitionsContainer.Instance.SaveDefinition(definition);
            }

            {
                string filename = "Scripts" +
                    Path.DirectorySeparatorChar + locale.ToString() +
                    Path.DirectorySeparatorChar + version.ToString() +
                    Path.DirectorySeparatorChar + "Inbound" +
                    Path.DirectorySeparatorChar + "0xFFFF.txt";
                if (!Directory.Exists(Path.GetDirectoryName(filename))) Directory.CreateDirectory(Path.GetDirectoryName(filename));
                if (!File.Exists(filename))
                {
                    string contents = "";
                    contents += "using (ScriptAPI) {\r\n";
                    contents += "\tAddShort(\"Packet Size\");\r\n";
                    contents += "\tAddUShort(\"MapleStory Version\");\r\n";
                    contents += "\tAddString(\"MapleStory Patch Location/Subversion\");\r\n";
                    contents += "\tAddField(\"Local Initializing Vector (IV)\", 4);\r\n";
                    contents += "\tAddField(\"Remote Initializing Vector (IV)\", 4);\r\n";
                    contents += "\tAddByte(\"MapleStory Locale\");\r\n";
                    contents += "}";
                    File.WriteAllText(filename, contents);
                }
            }

            var handshakePacket = new MaplePacket(arrivalTime, false, version, locale, 0xFFFF, definition == null ? "" : definition.Name, packet.ToArray(), (uint)0, BitConverter.ToUInt32(remoteIV, 0));

            return new KeyValuePair<MapleProtocol, MaplePacket>(session, handshakePacket);
        }
    }
}
