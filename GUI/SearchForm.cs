using MultiShark.Protocols;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;

using _BasePacket = MultiShark.Protocols.BasePacket<MultiShark.Protocols.IBaseOpcode>;
using _IBasePacket = MultiShark.Protocols.IBasePacket<MultiShark.Protocols.IBaseOpcode>;
using _BaseProtocol = MultiShark.Protocols.IBaseProtocol<MultiShark.Protocols.IBasePacket<MultiShark.Protocols.IBaseOpcode>, MultiShark.Protocols.IBaseOpcode>;

namespace MultiShark
{
    partial class SearchForm : DockContent
    {
        public SearchForm()
        {
            InitializeComponent();
            mSequenceHex.ByteProvider = new DynamicByteProvider(new ByteCollection());
            (mSequenceHex.ByteProvider as DynamicByteProvider).Changed += mSequenceHex_ByteProviderChanged;
        }

        public MainForm MainForm { get { return ParentForm as MainForm; } }
        public ComboBox ComboBox { get { return mOpcodeCombo; } }
        public HexBox HexBox { get { return mSequenceHex; } }

        public void RefreshOpcodes(bool pReselect)
        {
            var session = DockPanel.ActiveDocument as SessionForm;
            Opcode<IBaseOpcode> selected = pReselect && session != null && mOpcodeCombo.SelectedIndex >= 0 && session.Opcodes.Count > mOpcodeCombo.SelectedIndex ? session.Opcodes[mOpcodeCombo.SelectedIndex] : null;
            mOpcodeCombo.Items.Clear();
            if (session == null) return;
            session.UpdateOpcodeList();
            foreach (var op in session.Opcodes)
            {
                /*
                Definition definition = Config.Instance.GetDefinition(session.Build, session.Locale, op.Outbound, op.Header);
                int addedIndex = mOpcodeCombo.Items.Add(string.Format("{0} 0x{1:X4} {2}", (op.Outbound ? "Outbound  " : "Inbound   "), op.Header, definition == null || string.IsNullOrEmpty(definition.Name) ? "" : definition.Name));

                if (selected != null && selected.Outbound == op.Outbound && selected.Header == op.Header)
                {
                    mOpcodeCombo.SelectedIndex = addedIndex;
                }
                */
            }
        }

        private void mOpcodeCombo_SelectedIndexChanged(object pSender, EventArgs pArgs)
        {
            mNextOpcodeButton.Enabled = mPrevOpcodeButton.Enabled = mOpcodeCombo.SelectedIndex >= 0;
        }

        private void mNextOpcodeButton_Click(object pSender, EventArgs pArgs)
        {
            var session = DockPanel.ActiveDocument as SessionForm;
            if (session == null || mOpcodeCombo.SelectedIndex == -1)
                return;
            var search = session.Opcodes[mOpcodeCombo.SelectedIndex];
            int initialIndex = session.ListView.SelectedIndices.Count == 0 ? 0 : session.ListView.SelectedIndices[0] + 1;
            for (int index = initialIndex; index < session.ListView.Items.Count; ++index)
            {
                var lvi = session.ListView.Items[index];
                var packet = lvi.Tag as _IBasePacket;
                if (packet.Outbound == search.Outbound && packet.Opcode == search.Header)
                {
                    session.ListView.SelectedIndices.Clear();
                    session.ListView.SelectedIndices.Add(index);
                    lvi.EnsureVisible();
                    session.ListView.Focus();
                    return;
                }
            }
            MessageBox.Show("No further packets found with the selected opcode.", "End Of Search", MessageBoxButtons.OK, MessageBoxIcon.Information);
            session.ListView.Focus();
        }

        private void mSequenceHex_ByteProviderChanged(object pSender, EventArgs pArgs)
        {
            mNextSequenceButton.Enabled/* = mPrevSequenceButton.Enabled*/ = mSequenceHex.ByteProvider.Length > 0;
        }

        private void mSequenceHex_KeyPress(object pSender, KeyPressEventArgs pArgs)
        {
            if (pArgs.KeyChar == (char)Keys.Enter)
            {
                pArgs.Handled = true;
                NextSequence();
            }
        }

        private void mNextSequenceButton_Click(object pSender, EventArgs pArgs)
        {
            NextSequence();
        }

        private void NextSequence()
        {
            var session = DockPanel.ActiveDocument as SessionForm;
            if (session == null) return;
            int initialIndex = session.ListView.SelectedIndices.Count == 0 ? 0 : session.ListView.SelectedIndices[0];
            byte[] pattern = (mSequenceHex.ByteProvider as DynamicByteProvider).Bytes.ToArray();
            long startIndex = MainForm.DataForm.HexBox.SelectionLength > 0 ? MainForm.DataForm.HexBox.SelectionStart : -1;
            for (int index = initialIndex; index < session.ListView.Items.Count; ++index)
            {
                var lvi = session.ListView.Items[index];
                var packet = lvi.Tag as _IBasePacket;
                long searchIndex = startIndex + 1;
                bool found = false;
                while (searchIndex <= packet.Buffer.Length - pattern.Length)
                {
                    found = true;
                    for (int patternIndex = 0; found && patternIndex < pattern.Length; ++patternIndex) found = packet.Buffer[searchIndex + patternIndex] == pattern[patternIndex];
                    if (found) break;
                    ++searchIndex;
                }
                if (found)
                {
                    session.ListView.SelectedIndices.Clear();
                    session.ListView.SelectedIndices.Add(index);
                    lvi.EnsureVisible();
                    MainForm.DataForm.HexBox.SelectionStart = searchIndex;
                    MainForm.DataForm.HexBox.SelectionLength = pattern.Length;
                    MainForm.DataForm.HexBox.ScrollByteIntoView();
                    session.ListView.Focus();
                    return;
                }
                startIndex = -1;
            }
            MessageBox.Show("No further sequences found.", "End Of Search", MessageBoxButtons.OK, MessageBoxIcon.Information);
            session.ListView.Focus();
        }

        private void mPrevOpcodeButton_Click(object sender, EventArgs e)
        {
            var session = DockPanel.ActiveDocument as SessionForm;
            if (session == null || mOpcodeCombo.SelectedIndex == -1)
                return;
            var search = session.Opcodes[mOpcodeCombo.SelectedIndex];
            int initialIndex = session.ListView.SelectedIndices.Count == 0 ? 0 : session.ListView.SelectedIndices[0];
            for (int index = initialIndex - 1; index > 0; --index)
            {
                var lvi = session.ListView.Items[index];
                var packet = lvi.Tag as _IBasePacket;
                if (packet.Outbound == search.Outbound && packet.Opcode == search.Header)
                {
                    session.ListView.SelectedIndices.Clear();
                    session.ListView.SelectedIndices.Add(index);
                    lvi.EnsureVisible();
                    session.ListView.Focus();
                    return;
                }
            }
            MessageBox.Show("No further packets found with the selected opcode.", "End Of Search", MessageBoxButtons.OK, MessageBoxIcon.Information);
            session.ListView.Focus();
        }

        private void SearchForm_Load(object sender, EventArgs e)
        {

        }
    }
}
