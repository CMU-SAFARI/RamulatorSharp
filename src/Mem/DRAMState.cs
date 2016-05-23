using Ramulator.MemCtrl;
using Ramulator.MemReq;
using Ramulator.Sim;
using System;
using System.Collections.Generic;

namespace Ramulator.Mem
{
    public class NodeMachine
    {
        public enum Level
        {
            CHANNEL = 0,
            RANK,
            BANK,
            SUBARRAY,
            ROW,
            MAX
        }

        public NodeMachine[] Children;
        public uint id;
        public Level level;
        public List<ulong> OpenChildrenId;

        public NodeMachine Parent;

        public NodeMachine(NodeMachine parent, int level, uint id, uint[] fanOutArray)
        {
            Parent = parent;
            this.level = (Level)level;
            this.id = id;

            // initialize open children
            OpenChildrenId = new List<ulong>();

            // initialize children
            if (level == (int)Level.SUBARRAY)
            {
                Children = null; // a subarray has *NO* child objects
                return;
            }

            Children = new NodeMachine[fanOutArray[level + 1]];
            for (uint i = 0; i < Children.Length; i++)
                Children[i] = new NodeMachine(this, level + 1, i, fanOutArray);
        }

        public void Open(ulong[] addrArray, long cycles = 0)
        {
            Dbg.Assert((int)level <= (int)Level.SUBARRAY);

            // child id
            var childId = addrArray[(int)level + 1];

            // open child
            OpenChildrenId.Remove(childId);
            OpenChildrenId.Insert(0, childId); // Head is the location of the most recently used item

            // operate on children
            if (Children == null)
                return;

            Children[childId].Open(addrArray, cycles);
        }

        public void Close()
        {
            Dbg.Assert((int)level <= (int)Level.SUBARRAY);
            close_children();
            close_parent();
        }

        private void close_children()
        {
            // close children of the leaf node
            if (Children == null)
            {
                // There is no need to close a row if there are available row buffers
                Dbg.Assert(OpenChildrenId.Count <= Config.mem.max_row_buffer_count);

                // If full, remove the least recently used row buffer, which is at the tail. Otherwise leave the row buffer as active and use other row buffers.
                if (OpenChildrenId.Count == Config.mem.max_row_buffer_count)
                    OpenChildrenId.RemoveAt(OpenChildrenId.Count - 1);
                return;
            }

            // recursively close my children
            foreach (var i in OpenChildrenId)
                Children[i].close_children();

            OpenChildrenId.Clear();
        }

        public void close_sa(uint said)
        {
            Dbg.Assert((int)level <= (int)Level.SUBARRAY);

            // If all child are close, then close my id in the parent node
            if (close_sa_children_id(said))
                close_parent();
        }

        // Only close all the specified subarrays in all banks within a rank
        private bool close_sa_children_id(uint said)
        {
            // close children of the leaf node
            if (Children == null)
            {
                Dbg.Assert(OpenChildrenId.Count <= 1);
                if (id == said)
                {
                    OpenChildrenId.Clear();
                    return true;
                }
                return false;
            }

            // recursively close my children
            foreach (var i in OpenChildrenId)
            {
                if (Children[i].close_sa_children_id(said))
                    OpenChildrenId.Remove(i);
            }

            // No more open child
            if (OpenChildrenId.Count == 0)
                return true;
            return false;
        }

        private void close_parent()
        {
            if (Parent == null)
                return;

            // close me
            Parent.OpenChildrenId.Remove(id);
            if (Parent.OpenChildrenId.Count > 0)
                return;

            // recursively close my parent
            Parent.close_parent();
        }

        public NodeMachine Get(Level level, ulong[] addrArray)
        {
            if (this.level == level)
                return this;

            var childId = addrArray[(int)this.level + 1];
            return Children[childId].Get(level, addrArray);
        }
    }

    public class DRAMState
    {
        private readonly NodeMachine _channel;
        private readonly Random _rand = new Random(0);

        public DRAMState(uint cid, uint[] fanOutArray, MemCtrl.MemCtrl mctrl)
        {
            _channel = new NodeMachine(null, 0, cid, fanOutArray);
        }

        /* Decodes a request into a DRAM command */

        public Cmd Crack(Req req)
        {
            if (req.Type == ReqType.READ || req.Type == ReqType.WRITE)
                return crack_rw(req);

            if (req.Type == ReqType.REFRESH || req.Type == ReqType.REFRESH_BANK)
                return crack_refresh(req);

            if (req.Type != ReqType.COPY) return null;
            var cmd = new Cmd();
            cmd.Req = req;
            cmd.Addr = new MemAddr(req.Addr);

            // artifical remapping to test the maximum benefit of each copy mechanism
            switch (Config.mctrl.copy_method)
            {
                case COPY.MEMCPY:
                    // If we are using the pure base-per-req mode, then we shouldn't reach here b/c there shouldn't be any copy request.
                    throw new Exception("unknown state when cracking a memory request to commands.");

                case COPY.NAIVE_COPY:
                    cmd.Type = CmdType.BASE_INTER_SA_COPY;
                    break;

                case COPY.LISA_CLONE:
                    cmd.Type = CmdType.LINKS_INTER_SA_COPY;
                    break;

                case COPY.RC_INTER_SA:
                    cmd.Type = CmdType.ROWCLONE_INTER_SA_COPY;
                    break;

                case COPY.RC_INTER_BANK:
                    cmd.Type = CmdType.ROWCLONE_INTER_BANK_COPY;
                    break;

                case COPY.RC_INTRA_SA:
                    cmd.Type = CmdType.ROWCLONE_INTRA_SA_COPY;
                    break;

                case COPY.RC_PROB_SA:
                    cmd.Type = _rand.Next(1, 101) <= Config.mctrl.rc_prob_intra_sa
                        ? CmdType.ROWCLONE_INTRA_SA_COPY
                        : CmdType.ROWCLONE_INTER_SA_COPY;
                    break;

                case COPY.RC_LISA_PROB_SA:
                    cmd.Type = _rand.Next(1, 101) <= Config.mctrl.rc_prob_intra_sa
                        ? CmdType.ROWCLONE_INTRA_SA_COPY
                        : CmdType.LINKS_INTER_SA_COPY;
                    break;

                default:
                    throw new Exception("unknown copy method.");
            }
            return cmd;
        }

        /* Updates the DRAM state based on the DRAM command that has been issued */

        public void Update(CmdType c, MemAddr a, long cycles)
        {
            ulong[] addrArray = { a.cid, a.rid, a.bid, a.said, a.rowid };

            // activate || switch open subarray
            if (c == CmdType.ACT || c == CmdType.SEL_SA)
            {
                _channel.Open(addrArray, cycles);
                return;
            }

            // precharge
            if (c == CmdType.PRE_RANK || c == CmdType.PRE_BANK || c == CmdType.PRE_SA)
            {
                var level = NodeMachine.Level.RANK;

                if (c == CmdType.PRE_RANK)
                    level = NodeMachine.Level.RANK;
                else if (c == CmdType.PRE_BANK)
                    level = NodeMachine.Level.BANK;
                else if (c == CmdType.PRE_SA)
                    level = NodeMachine.Level.SUBARRAY;

                var node = _channel.Get(level, addrArray);
                node.Close();
                return;
            }

            // read/write + autoprecharge
            if (c == CmdType.RD_AP || c == CmdType.WR_AP)
            {
                var node =
                    _channel.Get(Config.mctrl.salp == SALP.NONE ? NodeMachine.Level.BANK : NodeMachine.Level.SUBARRAY,
                        addrArray);
                node.Close();
                return;
            }

            // More than one row buffer -- Put the most recently accessed row at the front of the list
            if (Config.mem.max_row_buffer_count <= 1 ||
                (c != CmdType.RD && c != CmdType.RD_AP && c != CmdType.WR && c != CmdType.WR_AP)) return;
            var saNode = _channel.Get(NodeMachine.Level.SUBARRAY, addrArray);
            Dbg.Assert(saNode.OpenChildrenId.Remove(a.rowid));
            saNode.OpenChildrenId.Insert(0, a.rowid);
        }

        public Cmd crack_rw(Req req)
        {
            var r = req.Type;
            var a = req.Addr;

            var cmd = new Cmd();
            cmd.Req = req;
            cmd.Addr = new MemAddr(a);

            ulong[] addrArray = { a.cid, a.rid, a.bid, a.said, a.rowid };
            var bank = _channel.Get(NodeMachine.Level.BANK, addrArray);
            var subarray = bank.Get(NodeMachine.Level.SUBARRAY, addrArray);

            switch (Config.mctrl.salp)
            {
                case SALP.NONE:
                    return base_cmd_issue(r, a, cmd, bank, subarray);

                case SALP.SALP1:
                    return salp1_cmd_issue(r, a, cmd, bank, subarray);

                case SALP.SALP2:
                    return salp2_cmd_issue(r, a, cmd, bank, subarray);

                case SALP.MASA:
                    return masa_cmd_issue(r, a, cmd, bank, subarray);

                default:
                    throw new Exception("Unrecognized SALP mode. Cannot probably decode and issue a DRAM command.");
            }
        }

        private static Cmd masa_cmd_issue(ReqType r, MemAddr a, Cmd cmd, NodeMachine bank, NodeMachine subarray)
        {
            var open_said = bank.OpenChildrenId;

            // subarray-miss
            if (!open_said.Contains(a.said))
            {
                cmd.Type = CmdType.ACT;
                return cmd;
            }

            // row-miss
            if (!subarray.OpenChildrenId.Contains(a.rowid))
            {
                cmd.Type = CmdType.PRE_SA;
                return cmd;
            }

            // not the most-recently-opened subarray
            if (open_said[0] != a.said && !Config.mctrl.b_piggyback_SEL_SA)
            {
                cmd.Type = CmdType.SEL_SA;
                return cmd;
            }

            // row-hit
            cmd.Type = r == ReqType.READ ? CmdType.RD : CmdType.WR;
            return cmd;
        }

        private static Cmd salp2_cmd_issue(ReqType r, MemAddr a, Cmd cmd, NodeMachine bank, NodeMachine subarray)
        {
            Dbg.Assert(bank.OpenChildrenId.Count <= 2);

            // closed bank
            if (bank.OpenChildrenId.Count == 0)
            {
                cmd.Type = CmdType.ACT;
                return cmd;
            }
            // open bank (one subarray)
            if (bank.OpenChildrenId.Count == 1)
            {
                // subarray-miss
                if (bank.OpenChildrenId[0] != a.said)
                {
                    cmd.Type = CmdType.ACT;
                    return cmd;
                }

                // row-miss
                if (subarray.OpenChildrenId[0] != a.rowid)
                {
                    cmd.Type = CmdType.PRE_SA;
                    return cmd;
                }

                // row-hit
                cmd.Type = r == ReqType.READ ? CmdType.RD : CmdType.WR;
                return cmd;
            }
            // open bank (two subarrays)
            // all subarray-miss
            if (!bank.OpenChildrenId.Contains(a.said))
            {
                cmd.Type = CmdType.PRE_SA;
                cmd.Addr.said = (uint)bank.OpenChildrenId[1]; // close least-recently-opened subarray
                return cmd;
            }

            // not the most-recently-opened subarray
            if (bank.OpenChildrenId[0] != a.said)
            {
                cmd.Type = CmdType.PRE_SA;
                cmd.Addr.said = (uint)bank.OpenChildrenId[1]; // close least-recently-opened subarray
                return cmd;
            }

            // row-miss
            if (subarray.OpenChildrenId[0] != a.rowid)
            {
                cmd.Type = CmdType.PRE_SA;
                return cmd;
            }

            // row-hit
            cmd.Type = r == ReqType.READ ? CmdType.RD : CmdType.WR;
            return cmd;
        }

        private static Cmd salp1_cmd_issue(ReqType r, MemAddr a, Cmd cmd, NodeMachine bank, NodeMachine subarray)
        {
            Dbg.Assert(bank.OpenChildrenId.Count <= 1);

            // closed bank
            if (bank.OpenChildrenId.Count == 0)
            {
                cmd.Type = CmdType.ACT;
                return cmd;
            }

            // subarray-miss
            if (bank.OpenChildrenId[0] != a.said)
            {
                cmd.Type = CmdType.PRE_SA;
                cmd.Addr.said = (uint)bank.OpenChildrenId[0]; // close the opened sibling
                return cmd;
            }

            // row-miss
            if (subarray.OpenChildrenId[0] != a.rowid)
            {
                cmd.Type = CmdType.PRE_SA;
                return cmd;
            }

            // row-hit
            cmd.Type = r == ReqType.READ ? CmdType.RD : CmdType.WR;
            return cmd;
        }

        private static Cmd base_cmd_issue(ReqType r, MemAddr a, Cmd cmd, NodeMachine bank, NodeMachine subarray)
        {
            Dbg.Assert(bank.OpenChildrenId.Count <= 1);

            // closed bank
            if (bank.OpenChildrenId.Count == 0)
            {
                cmd.Type = CmdType.ACT;
                return cmd;
            }

            // subarray-miss
            if (bank.OpenChildrenId[0] != a.said)
            {
                cmd.Type = CmdType.PRE_BANK;
                return cmd;
            }

            // row-miss
            if (subarray.OpenChildrenId[0] != a.rowid)
            {
                cmd.Type = CmdType.PRE_BANK;
                return cmd;
            }

            // row-hit
            cmd.Type = r == ReqType.READ ? CmdType.RD : CmdType.WR;
            return cmd;
        }

        public Cmd crack_refresh(Req req)
        {
            var r = req.Type;
            var a = req.Addr;
            var cmd = new Cmd();
            cmd.Req = req;
            cmd.Addr = new MemAddr(a);

            ulong[] addrArray = { a.cid, a.rid, a.bid, a.said, a.rowid };

            switch (r)
            {
                case ReqType.REFRESH:
                    var rank = _channel.Get(NodeMachine.Level.RANK, addrArray);
                    // open rank -- close it first
                    cmd.Type = rank.OpenChildrenId.Count != 0 ? CmdType.PRE_RANK : CmdType.REF_RANK;
                    break;

                case ReqType.REFRESH_BANK:
                    var bank = _channel.Get(NodeMachine.Level.BANK, addrArray);
                    // open bank
                    cmd.Type = bank.OpenChildrenId.Count != 0 ? CmdType.PRE_BANK : CmdType.REF_BANK;
                    break;

                default:
                    throw new Exception("Invalid refresh request to crack.");
            }

            return cmd;
        }

        public int get_num_open_subarray(uint rid, uint bid)
        {
            ulong[] addrArray = { 0, rid, bid, 0, 0 };
            var bank = _channel.Get(NodeMachine.Level.BANK, addrArray);
            return bank.OpenChildrenId.Count;
        }

        public NodeMachine GetChild(NodeMachine.Level level, ulong[] addrArray)
        {
            return _channel.Get(level, addrArray);
        }
    }
}
