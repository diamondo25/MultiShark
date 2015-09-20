using MapleLib.PacketLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace MultiShark.Protocols
{
    interface IBaseProtocol<out TPacket, out TOpcode>
        where TPacket : IBasePacket<TOpcode>
        where TOpcode : IBaseOpcode
    {
        string Name { get; }
        ColumnHeader[] GetListViewHeaders();
        IBaseStream<TPacket, TOpcode> InboundStream { get; }
        IBaseStream<TPacket, TOpcode> OutboundStream { get; }

        void EnrichSessionInfo(Dictionary<string, string> data);

        BaseDefinition GetDefinition(IBasePacket<IBaseOpcode> packet);

        string GetScriptLocation(IBasePacket<IBaseOpcode> packet);
        string GetCommonScriptLocation();

        Type Packet { get; }
        Type Opcode { get; }
    }

    abstract class BaseProtocol<TPacket, TOpcode> : IBaseProtocol<TPacket, TOpcode>
        where TPacket : IBasePacket<TOpcode>
        where TOpcode : IBaseOpcode
    {
        public Type Packet { get { return typeof(TPacket); } }
        public Type Opcode { get { return typeof(TOpcode); } }

        private string _name = "Base";
        public string Name { get { return _name; } }

        public virtual ColumnHeader[] GetListViewHeaders()
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
                    Text = "Name",
                    Width = 174
                }
            };
        }


        public IBaseStream<TPacket, TOpcode> InboundStream { get; protected set; }
        public IBaseStream<TPacket, TOpcode> OutboundStream { get; protected set; }

        public virtual void EnrichSessionInfo(Dictionary<string, string> data)
        {
        }

        public abstract BaseDefinition GetDefinition(IBasePacket<IBaseOpcode> packet);

        public abstract string GetScriptLocation(IBasePacket<IBaseOpcode> packet);
        public abstract string GetCommonScriptLocation();
    }
}
