using System.Windows.Forms;

namespace MultiShark
{
    public class StructureNode : TreeNode
    {
        public byte[] Buffer;
        public int Cursor;
        public int Length;

        public StructureNode(string pDisplay, byte[] pBuffer, int pCursor, int pLength)
            : base(pDisplay)
        {
            Buffer = pBuffer;
            Cursor = pCursor;
            Length = pLength;
        }
    }
}
