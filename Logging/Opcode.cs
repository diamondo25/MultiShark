using MultiShark.Protocols;
namespace MultiShark
{
    class Opcode<TOpcode> where TOpcode : IBaseOpcode
    {
        public TOpcode Header { get; private set; }
        public bool Outbound { get; private set; }

        public Opcode(bool pOutbound, TOpcode pHeader)
        {
            Outbound = pOutbound;
            Header = pHeader;
        }
    }
}