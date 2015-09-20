using ScriptNET;
using PacketDotNet;
using PacketDotNet.Utils;
using PacketDotNet.LLDP;
using SharpPcap;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Text;
using System.Linq;
using System.Windows.Forms;
using WeifenLuo.WinFormsUI.Docking;

using MapleLib.PacketLib;
using System.Text.RegularExpressions;
using MultiShark.Protocols;
using MultiShark.Protocols.MapleStory;

using _BasePacket = MultiShark.Protocols.BasePacket<MultiShark.Protocols.IBaseOpcode>;
using _IBasePacket = MultiShark.Protocols.IBasePacket<MultiShark.Protocols.IBaseOpcode>;
using _IBaseProtocol = MultiShark.Protocols.IBaseProtocol<MultiShark.Protocols.IBasePacket<MultiShark.Protocols.IBaseOpcode>, MultiShark.Protocols.IBaseOpcode>;

namespace MultiShark
{
    public enum Results
    {
        Continue,
        Terminated,
        CloseMe
    }

    partial class SessionForm : DockContent
    {
        private string _filename = null;
        private bool _terminated = false;
        private ushort _localPort = 0;
        private ushort _remotePort = 0;
        private ushort _proxyPort = 0;
        private uint _outboundSequence = 0;
        private uint _inboundSequence = 0;
        private Dictionary<uint, byte[]> _outboundBuffer = new Dictionary<uint, byte[]>();
        private Dictionary<uint, byte[]> _inboundBuffer = new Dictionary<uint, byte[]>();
        private List<IBasePacket<IBaseOpcode>> _packets = new List<IBasePacket<IBaseOpcode>>();
        private List<Opcode<IBaseOpcode>> _opcodes = new List<Opcode<IBaseOpcode>>();
        private int _socks5 = 0;
        
        private string _remoteEndpoint = "???";
        private string _localEndpoint = "???";
        private string _proxyEndpoint = "???";

        // Used for determining if the session did receive a packet at all, or if it just emptied its buffers
        public bool ClearedPackets { get; private set; }

        public MainForm MainForm { get { return ParentForm as MainForm; } }
        public ListView ListView { get { return mPacketList; } }
        public List<Opcode<IBaseOpcode>> Opcodes { get { return _opcodes; } }

        public bool Saved { get; private set; }

        private DateTime startTime;

        private _IBaseProtocol _protocol = null;

        internal SessionForm()
        {
            ClearedPackets = false;
            InitializeComponent();
            Saved = false;
        }

        public void UpdateOpcodeList()
        {
            _opcodes = _opcodes.OrderBy(a => a.Header).ToList();
        }


        public bool MatchTCPPacket(TcpPacket pTCPPacket)
        {
            if (_terminated) return false;
            if (pTCPPacket.SourcePort == _localPort && pTCPPacket.DestinationPort == (_proxyPort > 0 ? _proxyPort : _remotePort)) return true;
            if (pTCPPacket.SourcePort == (_proxyPort > 0 ? _proxyPort : _remotePort) && pTCPPacket.DestinationPort == _localPort) return true;
            return false;
        }

        public bool CloseMe(DateTime pTime)
        {
            if (!ClearedPackets && _packets.Count == 0 && (pTime - startTime).TotalSeconds >= 5)
            {
                return true;
            }
            return false;
        }

        public Results BufferTCPPacket(TcpPacket pTCPPacket, DateTime pArrivalTime)
        {
            if (pTCPPacket.Fin || pTCPPacket.Rst)
            {
                _terminated = true;
                Text += " (Terminated)";

                return _packets.Count == 0 ? Results.CloseMe : Results.Terminated;
            }
            if (pTCPPacket.Syn && !pTCPPacket.Ack)
            {
                _localPort = (ushort)pTCPPacket.SourcePort;
                _remotePort = (ushort)pTCPPacket.DestinationPort;
                _outboundSequence = (uint)(pTCPPacket.SequenceNumber + 1);
                Text = "Port " + _localPort + " - " + _remotePort;
                startTime = DateTime.Now;

                try
                {
                    _remoteEndpoint = ((PacketDotNet.IPv4Packet)pTCPPacket.ParentPacket).SourceAddress.ToString() + ":" + pTCPPacket.SourcePort.ToString();
                    _localEndpoint = ((PacketDotNet.IPv4Packet)pTCPPacket.ParentPacket).DestinationAddress.ToString() + ":" + pTCPPacket.DestinationPort.ToString();
                    Console.WriteLine("[CONNECTION] From {0} to {1}", _remoteEndpoint, _localEndpoint);

                    return Results.Continue;
                }
                catch
                {
                    return Results.CloseMe;
                }
            }
            if (pTCPPacket.Syn && pTCPPacket.Ack) { _inboundSequence = (uint)(pTCPPacket.SequenceNumber + 1); return Results.Continue; }
            if (pTCPPacket.PayloadData.Length == 0) return Results.Continue;
            if (_protocol == null)
            {
                byte[] tcpData = pTCPPacket.PayloadData;

                if (pTCPPacket.SourcePort == _localPort) _outboundSequence += (uint)tcpData.Length;
                else _inboundSequence += (uint)tcpData.Length;

                ushort length = (ushort)(BitConverter.ToUInt16(tcpData, 0) + 2);
                byte[] headerData = new byte[tcpData.Length];
                Buffer.BlockCopy(tcpData, 0, headerData, 0, tcpData.Length);

                PacketReader pr = new PacketReader(headerData);

                if (length != tcpData.Length || tcpData.Length < 13)
                {
                    if (_socks5 > 0 && _socks5 < 7)
                    {
                        if (pr.ReadByte() == 5 && pr.ReadByte() == 1)
                        {
                            pr.ReadByte();
                            _proxyEndpoint = _localEndpoint;
                            _localEndpoint = "";
                            switch (pr.ReadByte())
                            {
                                case 1://IPv4
                                    for (int i = 0; i < 4; i++)
                                    {
                                        _localEndpoint += pr.ReadByte();
                                        if (i < 3)
                                        {
                                            _localEndpoint += ".";
                                        }
                                    }
                                    break;
                                case 3://Domain
                                    //readInt - String Length
                                    //readAsciiString - Address
                                    break;
                                case 4://IPv6
                                    for (int i = 0; i < 16; i++)
                                    {
                                        pr.ReadByte();
                                    }
                                    break;
                            }
                            byte[] ports = new byte[2];
                            for (int i = 1; i >= 0; i--)
                            {
                                ports[i] = pr.ReadByte();
                            }
                            PacketReader portr = new PacketReader(ports);
                            _proxyPort = _remotePort;
                            _remotePort = portr.ReadUShort();
                            _localEndpoint += ":" + _remotePort;
                            Text = "Port " + _localPort + " - " + _remotePort + "(Proxy" + _proxyPort + ")";
                            Console.WriteLine("[socks5] From {0} to {1} (Proxy {2})", _remoteEndpoint, _localEndpoint, _proxyEndpoint);
                        }
                        _socks5++;
                        return Results.Continue;
                    }
                    else if (tcpData.Length == 3 && pr.ReadByte() == 5)
                    {
                        _socks5 = 1;
                        return Results.Continue;
                    }
                    Console.WriteLine("Connection on port {0} did not have a MapleStory Handshake", _localEndpoint);
                    return Results.CloseMe;
                }

                var kvp = MapleProtocol.ParseHandshake(pr.ToArray(), pArrivalTime);
                if (!kvp.HasValue)
                {
                    return Results.CloseMe;
                }
                _protocol = kvp.Value.Key;
                var hs = kvp.Value.Value;

                mPacketList.Items.Add(hs.GetListViewItem());
                _packets.Add(hs);


                ListView.Columns.Clear();
                ListView.Columns.AddRange(_protocol.GetListViewHeaders());
                MainForm.SearchForm.RefreshOpcodes(true);
            }
            if (pTCPPacket.SourcePort == _localPort) ProcessTCPPacket(pTCPPacket, ref _outboundSequence, _outboundBuffer, _protocol.OutboundStream, pArrivalTime);
            else ProcessTCPPacket(pTCPPacket, ref _inboundSequence, _inboundBuffer, _protocol.InboundStream, pArrivalTime);

            return Results.Continue;
        }

        private void ProcessTCPPacket(TcpPacket pTCPPacket, ref uint pSequence, Dictionary<uint, byte[]> pBuffer, IBaseStream<IBasePacket<IBaseOpcode>, IBaseOpcode> pStream, DateTime pArrivalDate)
        {
            if (pTCPPacket.SequenceNumber > pSequence)
            {
                byte[] data;

                while (pBuffer.TryGetValue(pSequence, out data))
                {
                    pBuffer.Remove(pSequence);
                    pStream.Append(data);
                    pSequence += (uint)data.Length;
                }
                if (pTCPPacket.SequenceNumber > pSequence) pBuffer[(uint)pTCPPacket.SequenceNumber] = pTCPPacket.PayloadData;
            }
            if (pTCPPacket.SequenceNumber < pSequence)
            {
                int difference = (int)(pSequence - pTCPPacket.SequenceNumber);
                if (difference > 0)
                {
                    byte[] data = pTCPPacket.PayloadData;
                    if (data.Length > difference)
                    {
                        pStream.Append(data, difference, data.Length - difference);
                        pSequence += (uint)(data.Length - difference);
                    }
                }
            }
            else if (pTCPPacket.SequenceNumber == pSequence)
            {
                byte[] data = pTCPPacket.PayloadData;
                pStream.Append(data);
                pSequence += (uint)data.Length;
            }

            IBasePacket<IBaseOpcode> packet;
            bool refreshOpcodes = false;
            try
            {
                mPacketList.BeginUpdate();

                while ((packet = pStream.Read(pArrivalDate)) != null)
                {
                    var bp = packet;
                    _packets.Add(bp);
                    /*
                    Definition definition = Config.Instance.GetDefinition(mBuild, mLocale, packet.Outbound, packet.Opcode);
                    if (!_opcodes.Exists(op => op.Outbound == packet.Outbound && op.Header == packet.Opcode))
                    {
                        _opcodes.Add(new Opcode(packet.Outbound, packet.Opcode));
                        refreshOpcodes = true;
                    }
                    if (definition != null && !mViewIgnoredMenu.Checked && definition.Ignore) continue;
                    */
                    if (packet.Outbound && !mViewOutboundMenu.Checked) continue;
                    if (!packet.Outbound && !mViewInboundMenu.Checked) continue;

                    var lvi = bp.GetListViewItem();
                    mPacketList.Items.Add(lvi);
                    if (mPacketList.SelectedItems.Count == 0) lvi.EnsureVisible();
                }

                mPacketList.EndUpdate();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                _terminated = true;
                Text += " (Terminated)";
                //MainForm.CloseSession(this);
                return;
            }

            if (DockPanel.ActiveDocument == this && refreshOpcodes) MainForm.SearchForm.RefreshOpcodes(true);
        }

        public void OpenReadOnly(string pFilename)
        {
            throw new NotImplementedException();
        }

        public void RefreshPackets()
        {

            var search = (MainForm.SearchForm.ComboBox.SelectedIndex >= 0 ? _opcodes[MainForm.SearchForm.ComboBox.SelectedIndex] : null);
            var previous = mPacketList.SelectedItems.Count > 0 ? mPacketList.Items[0] : null;
            _opcodes.Clear();
            mPacketList.Items.Clear();

            MainForm.DataForm.HexBox.ByteProvider = null;
            MainForm.StructureForm.Tree.Nodes.Clear();
            MainForm.PropertyForm.Properties.SelectedObject = null;

            if (!mViewOutboundMenu.Checked && !mViewInboundMenu.Checked) return;
            mPacketList.BeginUpdate();
            for (int index = 0; index < _packets.Count; ++index)
            {
                var packet = _packets[index];
                if (packet.Outbound && !mViewOutboundMenu.Checked) continue;
                if (!packet.Outbound && !mViewInboundMenu.Checked) continue;

                /*
                Definition definition = Config.Instance.GetDefinition(mBuild, mLocale, packet.Outbound, packet.Opcode);
                packet.Name = definition == null ? "" : definition.Name;
                if (!_opcodes.Exists(op => op.Outbound == packet.Outbound && op.Header == packet.Opcode)) _opcodes.Add(new Opcode(packet.Outbound, packet.Opcode));
                if (definition != null && !mViewIgnoredMenu.Checked && definition.Ignore) continue;
                */


                var bp = packet.GetListViewItem();
                mPacketList.Items.Add(bp);

                if (bp == previous) bp.Selected = true;
            }
            mPacketList.EndUpdate();
            MainForm.SearchForm.RefreshOpcodes(true);

            if (previous != null) previous.EnsureVisible();
        }

        public void ReselectPacket()
        {
            mPacketList_SelectedIndexChanged(null, null);
        }

        public void RunSaveCMD()
        {
            mFileSaveMenu.PerformClick();
        }

        private void mFileSaveMenu_Click(object pSender, EventArgs pArgs)
        {
            throw new NotImplementedException();
        }

        private void mFileExportMenu_Click(object pSender, EventArgs pArgs)
        {
            throw new NotImplementedException();
        }

        private void mViewCommonScriptMenu_Click(object pSender, EventArgs pArgs)
        {
            string scriptPath = "Scripts" + Path.DirectorySeparatorChar + _protocol.GetCommonScriptLocation();
            if (!Directory.Exists(Path.GetDirectoryName(scriptPath))) Directory.CreateDirectory(Path.GetDirectoryName(scriptPath));
            var script = new ScriptForm(scriptPath, null);
            script.FormClosed += CommonScript_FormClosed;
            script.Show(DockPanel, new Rectangle(MainForm.Location, new Size(600, 300)));
        }

        private void CommonScript_FormClosed(object pSender, FormClosedEventArgs pArgs)
        {
            if (mPacketList.SelectedIndices.Count == 0) return;
            var packet = mPacketList.SelectedItems[0].Tag as _IBasePacket;
            MainForm.StructureForm.ParsePacket(_protocol as _IBaseProtocol, packet);
            Activate();
        }

        private void mViewRefreshMenu_Click(object pSender, EventArgs pArgs) { RefreshPackets(); }
        private void mViewOutboundMenu_CheckedChanged(object pSender, EventArgs pArgs) { RefreshPackets(); }
        private void mViewInboundMenu_CheckedChanged(object pSender, EventArgs pArgs) { RefreshPackets(); }
        private void mViewIgnoredMenu_CheckedChanged(object pSender, EventArgs pArgs) { RefreshPackets(); }

        private void mPacketList_SelectedIndexChanged(object pSender, EventArgs pArgs)
        {
            if (mPacketList.SelectedItems.Count == 0)
            {
                MainForm.DataForm.HexBox.ByteProvider = null;
                MainForm.StructureForm.Tree.Nodes.Clear();
                MainForm.PropertyForm.Properties.SelectedObject = null;
                return;
            }
            var packet = mPacketList.SelectedItems[0].Tag as _IBasePacket;
            MainForm.DataForm.HexBox.ByteProvider = new DynamicByteProvider(packet.Buffer);
            MainForm.StructureForm.ParsePacket(_protocol as _IBaseProtocol, packet);
        }

        private void mPacketList_ItemActivate(object pSender, EventArgs pArgs)
        {
            if (mPacketList.SelectedIndices.Count == 0) return;

            var packet = mPacketList.SelectedItems[0].Tag as _IBasePacket;

            string scriptPath = "Scripts" + Path.DirectorySeparatorChar + _protocol.GetScriptLocation(packet);
            if (!Directory.Exists(Path.GetDirectoryName(scriptPath))) Directory.CreateDirectory(Path.GetDirectoryName(scriptPath));

            var script = new ScriptForm(scriptPath, packet);
            script.FormClosed += Script_FormClosed;
            script.Show(DockPanel, new Rectangle(MainForm.Location, new Size(600, 300)));
        }

        private void Script_FormClosed(object pSender, FormClosedEventArgs pArgs)
        {
            var script = pSender as ScriptForm;
            script.Packet.GetListViewItem().Selected = true;
            MainForm.StructureForm.ParsePacket(_protocol as IBaseProtocol<_BasePacket, IBaseOpcode>, script.Packet as _BasePacket);
            Activate();
        }

        bool openingContextMenu = false;
        private void mPacketContextMenu_Opening(object pSender, CancelEventArgs pArgs)
        {
            openingContextMenu = true;
            mPacketContextNameBox.Text = "";
            mPacketContextIgnoreMenu.Checked = false;
            if (mPacketList.SelectedItems.Count == 0) pArgs.Cancel = true;
            else
            {
                /*
                TPacket packet = mPacketList.SelectedItems[0] as BasePacket;
                Definition definition = Config.Instance.GetDefinition(mBuild, mLocale, packet.Outbound, packet.Opcode);
                if (definition != null)
                {
                    mPacketContextNameBox.Text = definition.Name;
                    mPacketContextIgnoreMenu.Checked = definition.Ignore;
                }
                */
            }
        }

        private void mPacketContextMenu_Opened(object pSender, EventArgs pArgs)
        {
            mPacketContextNameBox.Focus();
            mPacketContextNameBox.SelectAll();

            mPacketList.SelectedItems[0].EnsureVisible();
            openingContextMenu = false;
        }

        private void mPacketContextNameBox_KeyDown(object pSender, KeyEventArgs pArgs)
        {
            if (pArgs.Modifiers == Keys.None && pArgs.KeyCode == Keys.Enter)
            {
                /*
                TPacket packet = mPacketList.SelectedItems[0] as BasePacket;
                throw new NotImplementedException();
                Definition definition = Config.Instance.GetDefinition(mBuild, mLocale, packet.Outbound, packet.Opcode);
                if (definition == null)
                {
                    definition = new Definition();
                    definition.Build = mBuild;
                    definition.Outbound = packet.Outbound;
                    definition.Opcode = packet.Opcode;
                    definition.Locale = mLocale;
                }
                definition.Name = mPacketContextNameBox.Text;
                DefinitionsContainer.Instance.SaveDefinition(definition);
                pArgs.SuppressKeyPress = true;
                mPacketContextMenu.Close();
                RefreshPackets();

                packet.EnsureVisible();
                */
            }
        }

        private void mPacketContextIgnoreMenu_CheckedChanged(object pSender, EventArgs pArgs)
        {
            if (openingContextMenu) return;
            /*
            TPacket packet = mPacketList.SelectedItems[0] as BasePacket;
            throw new NotImplementedException();
            Definition definition = Config.Instance.GetDefinition(mBuild, mLocale, packet.Outbound, packet.Opcode);
            if (definition == null)
            {
                definition = new Definition();
                definition.Locale = mLocale;
                definition.Build = mBuild;
                definition.Outbound = packet.Outbound;
                definition.Opcode = packet.Opcode;
                definition.Locale = mLocale;
            }
            definition.Ignore = mPacketContextIgnoreMenu.Checked;
            DefinitionsContainer.Instance.SaveDefinition(definition);

            int newIndex = packet.Index - 1;
            for (var i = packet.Index - 1; i > 0; i--)
            {
                var pack = mPacketList.Items[i] as BasePacket;
                var def = Config.Instance.GetDefinition(mBuild, mLocale, pack.Outbound, pack.Opcode);
                if (def == definition)
                {
                    newIndex--;
                }
            }

            RefreshPackets();


            if (newIndex != 0 && mPacketList.Items[newIndex] != null)
            {
                packet = mPacketList.Items[newIndex] as BasePacket;
                packet.Selected = true;
                packet.EnsureVisible();
            }
            */
        }

        private void SessionForm_Load(object sender, EventArgs e)
        {

        }

        private void mMenu_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {

        }

        private void sessionInformationToolStripMenuItem_Click(object sender, EventArgs e)
        {
            /*
            SessionInformation si = new SessionInformation();
            si.txtVersion.Text = mBuild.ToString();
            si.txtPatchLocation.Text = mPatchLocation;
            si.txtLocale.Text = mLocale.ToString();
            si.txtAdditionalInfo.Text = "Connection info:\r\n" + _localEndpoint + " <-> " + _remoteEndpoint + (_proxyEndpoint != "???" ? "\r\nProxy:" + _proxyEndpoint : "");

            if (mLocale == 1 || mLocale == 2)
            {
                si.txtAdditionalInfo.Text += "\r\nRecording session of a MapleStory Korea" + (mLocale == 2 ? " Test" : "") + " server.\r\nAdditional KMS info:\r\n";

                try
                {
                    int test = int.Parse(mPatchLocation);
                    ushort maplerVersion = (ushort)(test & 0x7FFF);
                    int extraOption = (test >> 15) & 1;
                    int subVersion = (test >> 16) & 0xFF;
                    si.txtAdditionalInfo.Text += "Real Version: " + maplerVersion + "\r\nSubversion: " + subVersion + "\r\nExtra flag: " + extraOption;
                }
                catch { }
            }

            si.Show();
             * */
        }

        private void sendpropertiesToolStripMenuItem_Click(object sender, EventArgs e)
        {
        }

        private void recvpropertiesToolStripMenuItem_Click(object sender, EventArgs e)
        {
        }

        private void removeLoggedPacketsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (MessageBox.Show("Are you sure you want to delete all logged packets?", "!!", MessageBoxButtons.YesNo) == System.Windows.Forms.DialogResult.No) return;

            ClearedPackets = true;

            _packets.Clear();
            ListView.Items.Clear();
            _opcodes.Clear();
            RefreshPackets();
        }

        private void mFileSeparatorMenu_Click(object sender, EventArgs e)
        {

        }

        private void thisPacketOnlyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (mPacketList.SelectedItems.Count == 0) return;
            var lvi = mPacketList.SelectedItems[0];
            var packet = lvi.Tag as _IBasePacket;
            var index = lvi.Index;
            _packets.Remove(packet);

            lvi.Selected = false;
            if (index > 0)
            {
                index--;
                ((_BasePacket)_packets[index]).GetListViewItem().Selected = true;
            }
            RefreshPackets();
        }

        private void allBeforeThisOneToolStripMenuItem_Click(object sender, EventArgs e)
        {
        }

        private void onlyVisibleToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (mPacketList.SelectedItems.Count == 0) return;
            var packet = mPacketList.SelectedItems[0];

            for (int i = 0; i < packet.Index; i++)
                _packets.Remove(mPacketList.Items[i].Tag as _IBasePacket);
            RefreshPackets();
        }

        private void allToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (mPacketList.SelectedItems.Count == 0) return;
            var packet = mPacketList.SelectedItems[0].Tag as _IBasePacket;

            _packets.RemoveRange(0, _packets.FindIndex((p) => { return p == packet; }));
            RefreshPackets();
        }

        private void onlyVisibleToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            if (mPacketList.SelectedItems.Count == 0) return;
            var packet = mPacketList.SelectedItems[0];

            for (int i = packet.Index + 1; i < mPacketList.Items.Count; i++)
                _packets.Remove(mPacketList.Items[i].Tag as _IBasePacket);
            RefreshPackets();
        }

        private void allToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            if (mPacketList.SelectedItems.Count == 0) return;
            var packet = mPacketList.SelectedItems[0].Tag as _IBasePacket;
            var startIndex = _packets.FindIndex((p) => { return p == packet; }) + 1;
            _packets.RemoveRange(startIndex, _packets.Count - startIndex);
            RefreshPackets();
        }

    }
}
