using System.Windows.Forms;

namespace MultiShark
{
    public sealed class DoubleBufferedListView : ListView
    {
        public DoubleBufferedListView()
            : base()
        {
            DoubleBuffered = true;
        }
    }
}