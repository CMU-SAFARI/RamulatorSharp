using System.Collections.Generic;
using Ramulator.MemCtrl;
using Ramulator.Sim;

namespace Ramulator.Mem
{
    public class TimingNode
    {
        public DDR3DRAM.Timing tc;

        public uint id;
        public Level level;
        public TimingNode[] Children;

        public uint[,] Constrs;
        public uint[,] SiblingConstrs;

        // Fast SA
        public uint[,] FastConstrs;
        public uint[,] FastSiblingConstrs;

        public long[] Next;
        public long[] Prev;

        public long NextFaw;
        public Queue<long> PrevFaw;

        public enum Level
        {
            CHANNEL = 0,
            RANK,
            BANK,
            SUBARRAY,
            MAX
        }

        public TimingNode(DDR3DRAM.Timing tc, int level, uint id, uint[] fanOutArray, uint[][,] constrsArray,
                          uint[][,] siblingConstrsArray)
        {
            this.tc = tc;
            this.level = (Level)level;
            this.id = id;

            // assign timing constraints
            Constrs = constrsArray[level];
            SiblingConstrs = siblingConstrsArray[level];

            // initialize timestamps
            Next = new long[(int)CmdType.MAX];
            for (int i = 0; i < Next.Length; i++)
                Next[i] = -1;

            Prev = new long[(int)CmdType.MAX];
            for (int i = 0; i < Prev.Length; i++)
                Prev[i] = -1;

            if (this.level == Level.RANK)
            {
                NextFaw = -1;

                PrevFaw = new Queue<long>(4);
                PrevFaw.Enqueue(-1);
                PrevFaw.Enqueue(-1);
                PrevFaw.Enqueue(-1);
                PrevFaw.Enqueue(-1);
            }

            // initialize children
            if (level + 1 == (int)Level.MAX)
            {
                Children = null;
                return;
            }

            Children = new TimingNode[fanOutArray[level + 1]];
            for (uint i = 0; i < Children.Length; i++)
                Children[i] = new TimingNode(tc, level + 1, i, fanOutArray, constrsArray, siblingConstrsArray);
        }

        // Timing node with two sets of timing constraints
        public TimingNode(DDR3DRAM.Timing tc, int level, uint id, uint[] fanOutArray, uint[][,] constrsArray,
                          uint[][,] siblingConstrsArray, uint[][,] fastConstrsArray, uint[][,] fastSiblingConstrsArray)
        {
            this.tc = tc;
            this.level = (Level)level;
            this.id = id;

            // assign timing constraints
            Constrs = constrsArray[level];
            SiblingConstrs = siblingConstrsArray[level];
            FastConstrs = fastConstrsArray[level];
            FastSiblingConstrs = fastSiblingConstrsArray[level];

            // initialize timestamps
            Next = new long[(int)CmdType.MAX];
            for (int i = 0; i < Next.Length; i++)
                Next[i] = -1;

            Prev = new long[(int)CmdType.MAX];
            for (int i = 0; i < Prev.Length; i++)
                Prev[i] = -1;

            if (this.level == Level.RANK)
            {
                NextFaw = -1;

                PrevFaw = new Queue<long>(4);
                PrevFaw.Enqueue(-1);
                PrevFaw.Enqueue(-1);
                PrevFaw.Enqueue(-1);
                PrevFaw.Enqueue(-1);
            }

            // initialize children
            if (level + 1 == (int)Level.MAX)
            {
                Children = null;
                return;
            }

            Children = new TimingNode[fanOutArray[level + 1]];
            for (uint i = 0; i < Children.Length; i++)
                Children[i] = new TimingNode(tc, level + 1, i, fanOutArray, constrsArray, siblingConstrsArray, fastConstrsArray, fastSiblingConstrsArray);
        }

        public bool Check(long cycles, int cmd, uint[] addrArray)
        {
            // check
            if (Next[cmd] != -1 && cycles < Next[cmd])
            {
                return false;
            }

            // check tFAW
            if (level == Level.RANK && cmd == (int)CmdType.ACT)
            {
                if (NextFaw != -1 && cycles < NextFaw)
                    return false;
            }

            // no children; must have passed all recursive tests
            if (Children == null)
                return true;

            // check child
            uint childId = addrArray[(int)level + 1];
            return Children[childId].Check(cycles, cmd, addrArray);
        }

        public void Update(long cycles, int cmd, uint[] addrArray, bool villaCache = false, bool chargeCacheHit = false)
        {
            // update timing for future cmds; i am a sibling of the target node
            if (id != addrArray[(int)level])
            {
                update_siblings(cycles, cmd);
                return;
            }

            // update timing for future cmds; i am the target node
            Prev[cmd] = cycles;
            for (int i = 0; i < (int)CmdType.MAX; i++)
                update_dram_cmd_constraint(cycles, cmd, villaCache, chargeCacheHit, i);

            // update tFAW
            if (level == Level.RANK && cmd == (int)CmdType.ACT)
            {
                PrevFaw.Dequeue();
                PrevFaw.Enqueue(cycles);
                Dbg.Assert(PrevFaw.Count == 4);

                long firstFaw = PrevFaw.Peek();
                if (firstFaw != -1)
                {
                    long horizon = firstFaw + tc.tFAW;
                    if (horizon > NextFaw)
                        NextFaw = horizon;
                }
            }

            // no children; must have updated all relevant nodes
            if (Children == null)
                return;

            // update all children
            foreach (TimingNode t in Children)
                t.Update(cycles, cmd, addrArray, villaCache, chargeCacheHit);
        }

        private void update_dram_cmd_constraint(long cycles, int cmd, bool villaCache, bool chargeCacheHit, int i)
        {
            long horizon = cycles;
            long constraint = villaCache | chargeCacheHit ? FastConstrs[cmd, i] : Constrs[cmd, i];

            // Update the timing
            horizon += constraint;
            if (horizon > Next[i])
                Next[i] = horizon;
        }

        private void update_siblings(long cycles, int cmd)
        {
            for (int i = 0; i < (int)CmdType.MAX; i++)
            {
                long horizon = cycles + SiblingConstrs[cmd, i];
                if (horizon > Next[i])
                    Next[i] = horizon;
            }
        }

        public bool check_and_time(long cycles, int cmd, uint[] addrArray, ref long timeDiff)
        {
            // check
            if (Next[cmd] != -1 && cycles < Next[cmd])
            {
                timeDiff = Next[cmd] - cycles;
                return false;
            }

            // check tFAW
            if (level == Level.RANK && cmd == (int)CmdType.ACT)
            {
                if (NextFaw != -1 && cycles < NextFaw)
                    return false;
            }

            // no children; must have passed all recursive tests
            if (Children == null)
                return true;

            // check child
            uint childId = addrArray[(int)level + 1];
            return Children[childId].check_and_time(cycles, cmd, addrArray, ref timeDiff);
        }
    }

    public class DRAMTiming
    {
        private DDR3DRAM.Timing tc;
        public uint[] ServiceArray;
        public uint[][,] ConstrsArray;
        public uint[][,] SiblingConstrsArray;

        public TimingNode Channel;

        public DRAMTiming(DDR3DRAM.Timing tc, uint cid, uint[] fanOutArray)
        {
            this.tc = tc;

            init_service_array();
            init_constrs_array();
            init_sibling_constrs_array();

            Channel = new TimingNode(tc, 0, cid, fanOutArray, ConstrsArray, SiblingConstrsArray);
        }

        public DRAMTiming(DDR3DRAM.Timing tc, uint cid, uint[] fanOutArray, DRAMTiming fastTiming)
        {
            this.tc = tc;

            init_service_array();
            init_constrs_array();
            init_sibling_constrs_array();

            // Heterogeneous DRAM: VarIabLe LAtency DRAM (VILLA). Add a fast subarray in every bank.
            // ChargeCache. Provide fast timing parameters to be used on ChargeCache hit.
            Channel = new TimingNode(tc, 0, cid, fanOutArray, ConstrsArray, SiblingConstrsArray,
                    fastTiming.ConstrsArray, fastTiming.SiblingConstrsArray);
            
        }

        private void init_service_array()
        {
            ServiceArray = new uint[(int)CmdType.MAX];
            uint[] s = ServiceArray;

            s[(int)CmdType.ACT] = tc.tRCD;
            s[(int)CmdType.SEL_SA] = tc.tSCD;
            s[(int)CmdType.PRE_RANK] = tc.tRP_rank;
            s[(int)CmdType.PRE_BANK] = tc.tRP;
            s[(int)CmdType.PRE_SA] = tc.tRP;
            s[(int)CmdType.REF_RANK] = tc.tRFC;
            s[(int)CmdType.REF_BANK] = tc.tRFCpb;
            s[(int)CmdType.RD] = tc.tCL;
            s[(int)CmdType.WR] = tc.tCWL;
            s[(int)CmdType.RD_AP] = tc.tCL + tc.tRTP + tc.tRP;
            s[(int)CmdType.WR_AP] = tc.tCWL + tc.tBL + tc.tWR + tc.tRP;
            s[(int)CmdType.ROWCLONE_INTRA_SA_COPY] = tc.tRC_INTRA_SA_COPY;
            s[(int)CmdType.ROWCLONE_INTER_SA_COPY] = tc.tRC_INTER_SA_COPY;
            s[(int)CmdType.ROWCLONE_INTER_BANK_COPY] = tc.tRC_INTER_BANK_COPY;
            s[(int)CmdType.LINKS_INTER_SA_COPY] = tc.tLISA_INTER_SA_COPY;
            s[(int)CmdType.BASE_INTER_SA_COPY] = tc.tNAIVE_BLOCK_INTER_SA_COPY;
        }

        private void init_constrs_array()
        {
            ConstrsArray = new uint[(int)TimingNode.Level.MAX][,];
            for (int i = 0; i < ConstrsArray.Length; i++)
            {
                ConstrsArray[i] = new uint[(int)CmdType.MAX, (int)CmdType.MAX];

                for (int j = 0; j < (int)CmdType.MAX; j++)
                    for (int k = 0; k < (int)CmdType.MAX; k++)
                        ConstrsArray[i][j, k] = 1;
            }

            uint[,] c;

            /*** Channel ***/
            c = ConstrsArray[(int)TimingNode.Level.CHANNEL];

            // Default locks the channel
            c[(int)CmdType.BASE_INTER_SA_COPY, (int)CmdType.ACT] = tc.tNAIVE_BLOCK_INTER_SA_COPY;
            c[(int)CmdType.BASE_INTER_SA_COPY, (int)CmdType.RD] = tc.tNAIVE_BLOCK_INTER_SA_COPY;
            c[(int)CmdType.BASE_INTER_SA_COPY, (int)CmdType.WR] = tc.tNAIVE_BLOCK_INTER_SA_COPY;
            c[(int)CmdType.BASE_INTER_SA_COPY, (int)CmdType.RD_AP] = tc.tNAIVE_BLOCK_INTER_SA_COPY;
            c[(int)CmdType.BASE_INTER_SA_COPY, (int)CmdType.WR_AP] = tc.tNAIVE_BLOCK_INTER_SA_COPY;
            c[(int)CmdType.BASE_INTER_SA_COPY, (int)CmdType.PRE_RANK] = tc.tNAIVE_BLOCK_INTER_SA_COPY;
            c[(int)CmdType.BASE_INTER_SA_COPY, (int)CmdType.PRE_BANK] = tc.tNAIVE_BLOCK_INTER_SA_COPY;
            c[(int)CmdType.BASE_INTER_SA_COPY, (int)CmdType.PRE_SA] = tc.tNAIVE_BLOCK_INTER_SA_COPY;

            /*** Rank ***/
            c = ConstrsArray[(int)TimingNode.Level.RANK];

            c[(int)CmdType.ACT, (int)CmdType.ACT] = tc.tRRD;
            c[(int)CmdType.ACT, (int)CmdType.PRE_RANK] = tc.tRAS;

            // Per-bank refresh constraint as specified in LPDDR
            c[(int)CmdType.ACT, (int)CmdType.REF_BANK] = tc.tRRD;
            c[(int)CmdType.REF_BANK, (int)CmdType.ACT] = tc.tRRD;

            // There is a special knob to allow REFpb to occur in parallel
            c[(int)CmdType.REF_BANK, (int)CmdType.REF_BANK] = (uint)(tc.tRFCpb * (1 - Config.mctrl.refpb_overlap_factor));

            // Precharge rank
            c[(int)CmdType.PRE_RANK, (int)CmdType.ACT] = tc.tRP_rank;
            c[(int)CmdType.PRE_RANK, (int)CmdType.REF_RANK] = tc.tRP_rank;
            c[(int)CmdType.PRE_RANK, (int)CmdType.REF_BANK] = tc.tRP_rank;

            // Precharge bank
            c[(int)CmdType.PRE_BANK, (int)CmdType.REF_RANK] = tc.tRP;

            // Make sure refresh is finished before the next refresh
            c[(int)CmdType.REF_RANK, (int)CmdType.PRE_RANK] = tc.tRFC;
            c[(int)CmdType.REF_RANK, (int)CmdType.REF_RANK] = tc.tRFC;

            c[(int)CmdType.RD, (int)CmdType.RD] = tc.tCCD;
            c[(int)CmdType.RD, (int)CmdType.RD_AP] = tc.tCCD;
            c[(int)CmdType.RD_AP, (int)CmdType.RD] = tc.tCCD;
            c[(int)CmdType.RD_AP, (int)CmdType.RD_AP] = tc.tCCD;

            c[(int)CmdType.WR, (int)CmdType.WR] = tc.tCCD;
            c[(int)CmdType.WR, (int)CmdType.WR_AP] = tc.tCCD;
            c[(int)CmdType.WR_AP, (int)CmdType.WR] = tc.tCCD;
            c[(int)CmdType.WR_AP, (int)CmdType.WR_AP] = tc.tCCD;

            c[(int)CmdType.RD, (int)CmdType.WR] = tc.tRTW;
            c[(int)CmdType.RD, (int)CmdType.WR_AP] = tc.tRTW;
            c[(int)CmdType.RD_AP, (int)CmdType.WR] = tc.tRTW;
            c[(int)CmdType.RD_AP, (int)CmdType.WR_AP] = tc.tRTW;

            c[(int)CmdType.WR, (int)CmdType.RD] = tc.tCWL + tc.tBL + tc.tWTR;
            c[(int)CmdType.WR, (int)CmdType.RD_AP] = tc.tCWL + tc.tBL + tc.tWTR;
            c[(int)CmdType.WR_AP, (int)CmdType.RD] = tc.tCWL + tc.tBL + tc.tWTR;
            c[(int)CmdType.WR_AP, (int)CmdType.RD_AP] = tc.tCWL + tc.tBL + tc.tWTR;

            // RowClone uses the internal data bus
            c[(int)CmdType.ROWCLONE_INTER_SA_COPY, (int)CmdType.ACT] = tc.tRC_INTER_SA_COPY;
            c[(int)CmdType.ROWCLONE_INTER_SA_COPY, (int)CmdType.RD] = tc.tRC_INTER_SA_COPY;
            c[(int)CmdType.ROWCLONE_INTER_SA_COPY, (int)CmdType.WR] = tc.tRC_INTER_SA_COPY;
            c[(int)CmdType.ROWCLONE_INTER_SA_COPY, (int)CmdType.RD_AP] = tc.tRC_INTER_SA_COPY;
            c[(int)CmdType.ROWCLONE_INTER_SA_COPY, (int)CmdType.WR_AP] = tc.tRC_INTER_SA_COPY;
            c[(int)CmdType.ROWCLONE_INTER_SA_COPY, (int)CmdType.PRE_BANK] = tc.tRC_INTER_SA_COPY;
            c[(int)CmdType.ROWCLONE_INTER_SA_COPY, (int)CmdType.PRE_SA] = tc.tRC_INTER_SA_COPY;

            c[(int)CmdType.ROWCLONE_INTER_BANK_COPY, (int)CmdType.ACT] = tc.tRC_INTER_BANK_COPY;
            c[(int)CmdType.ROWCLONE_INTER_BANK_COPY, (int)CmdType.RD] = tc.tRC_INTER_BANK_COPY;
            c[(int)CmdType.ROWCLONE_INTER_BANK_COPY, (int)CmdType.WR] = tc.tRC_INTER_BANK_COPY;
            c[(int)CmdType.ROWCLONE_INTER_BANK_COPY, (int)CmdType.RD_AP] = tc.tRC_INTER_BANK_COPY;
            c[(int)CmdType.ROWCLONE_INTER_BANK_COPY, (int)CmdType.WR_AP] = tc.tRC_INTER_BANK_COPY;
            c[(int)CmdType.ROWCLONE_INTER_BANK_COPY, (int)CmdType.PRE_BANK] = tc.tRC_INTER_BANK_COPY;
            c[(int)CmdType.ROWCLONE_INTER_BANK_COPY, (int)CmdType.PRE_SA] = tc.tRC_INTER_BANK_COPY;

            /*** Bank ***/
            c = ConstrsArray[(int)TimingNode.Level.BANK];

            c[(int)CmdType.ACT, (int)CmdType.PRE_BANK] = tc.tRAS;
            c[(int)CmdType.PRE_BANK, (int)CmdType.ACT] = tc.tRP;
            c[(int)CmdType.RD, (int)CmdType.PRE_BANK] = tc.tRTP; // no tbl and tcl?
            c[(int)CmdType.WR, (int)CmdType.PRE_BANK] = tc.tCWL + tc.tBL + tc.tWR;

            // Refresh
            c[(int)CmdType.PRE_BANK, (int)CmdType.REF_BANK] = tc.tRP;
            c[(int)CmdType.REF_BANK, (int)CmdType.PRE_BANK] = tc.tRFCpb;
            c[(int)CmdType.REF_BANK, (int)CmdType.REF_BANK] = tc.tRFCpb;

            // Different subarray levels
            if (Config.mctrl.salp == SALP.NONE)
            {
                c[(int)CmdType.ACT, (int)CmdType.ACT] = tc.tRC;
                c[(int)CmdType.ACT, (int)CmdType.RD] = tc.tRCD;
                c[(int)CmdType.ACT, (int)CmdType.RD_AP] = tc.tRCD;
                c[(int)CmdType.ACT, (int)CmdType.WR] = tc.tRCD;
                c[(int)CmdType.ACT, (int)CmdType.WR_AP] = tc.tRCD;
                c[(int)CmdType.RD_AP, (int)CmdType.ACT] = tc.tRTP + tc.tRP;
                c[(int)CmdType.WR_AP, (int)CmdType.ACT] = tc.tCWL + tc.tBL + tc.tWR + tc.tRP;
            }
            else if (Config.mctrl.salp == SALP.SALP1 || Config.mctrl.salp == SALP.SALP2)
            {
                c[(int)CmdType.ACT, (int)CmdType.RD] = tc.tRCD;
                c[(int)CmdType.ACT, (int)CmdType.RD_AP] = tc.tRCD;
                c[(int)CmdType.ACT, (int)CmdType.WR] = tc.tRCD;
                c[(int)CmdType.ACT, (int)CmdType.WR_AP] = tc.tRCD;
            }
            else if (Config.mctrl.salp == SALP.MASA)
            {
                // None for now
            }

            // CLONING
            c[(int)CmdType.LINKS_INTER_SA_COPY, (int)CmdType.ACT] = tc.tLISA_INTER_SA_COPY;
            c[(int)CmdType.LINKS_INTER_SA_COPY, (int)CmdType.RD] = tc.tLISA_INTER_SA_COPY;
            c[(int)CmdType.LINKS_INTER_SA_COPY, (int)CmdType.WR] = tc.tLISA_INTER_SA_COPY;
            c[(int)CmdType.LINKS_INTER_SA_COPY, (int)CmdType.RD_AP] = tc.tLISA_INTER_SA_COPY;
            c[(int)CmdType.LINKS_INTER_SA_COPY, (int)CmdType.WR_AP] = tc.tLISA_INTER_SA_COPY;
            c[(int)CmdType.LINKS_INTER_SA_COPY, (int)CmdType.PRE_BANK] = tc.tLISA_INTER_SA_COPY;
            c[(int)CmdType.LINKS_INTER_SA_COPY, (int)CmdType.PRE_SA] = tc.tLISA_INTER_SA_COPY;

            c[(int)CmdType.ROWCLONE_INTRA_SA_COPY, (int)CmdType.ACT] = tc.tRC_INTRA_SA_COPY;
            c[(int)CmdType.ROWCLONE_INTRA_SA_COPY, (int)CmdType.RD] = tc.tRC_INTRA_SA_COPY;
            c[(int)CmdType.ROWCLONE_INTRA_SA_COPY, (int)CmdType.WR] = tc.tRC_INTRA_SA_COPY;
            c[(int)CmdType.ROWCLONE_INTRA_SA_COPY, (int)CmdType.RD_AP] = tc.tRC_INTRA_SA_COPY;
            c[(int)CmdType.ROWCLONE_INTRA_SA_COPY, (int)CmdType.WR_AP] = tc.tRC_INTRA_SA_COPY;
            c[(int)CmdType.ROWCLONE_INTRA_SA_COPY, (int)CmdType.PRE_BANK] = tc.tRC_INTRA_SA_COPY;
            c[(int)CmdType.ROWCLONE_INTRA_SA_COPY, (int)CmdType.PRE_SA] = tc.tRC_INTRA_SA_COPY;

            /*** Subarray ***/
            c = ConstrsArray[(int)TimingNode.Level.SUBARRAY];

            if (Config.mctrl.salp == SALP.NONE)
            {
                // No timing constraints (subarray-oblivious)
            }
            else
            {
                c[(int)CmdType.ACT, (int)CmdType.ACT] = tc.tRC;
                c[(int)CmdType.ACT, (int)CmdType.PRE_SA] = tc.tRAS;
                c[(int)CmdType.PRE_SA, (int)CmdType.ACT] = tc.tRP;
                c[(int)CmdType.PRE_SA, (int)CmdType.REF_RANK] = tc.tRP;
                c[(int)CmdType.PRE_SA, (int)CmdType.REF_BANK] = tc.tRP;

                c[(int)CmdType.ACT, (int)CmdType.RD] = tc.tRCD;
                c[(int)CmdType.ACT, (int)CmdType.RD_AP] = tc.tRCD;
                c[(int)CmdType.ACT, (int)CmdType.WR] = tc.tRCD;
                c[(int)CmdType.ACT, (int)CmdType.WR_AP] = tc.tRCD;

                c[(int)CmdType.RD, (int)CmdType.PRE_SA] = tc.tRTP;
                c[(int)CmdType.WR, (int)CmdType.PRE_SA] = tc.tCWL + tc.tBL + tc.tWR;
                c[(int)CmdType.RD_AP, (int)CmdType.ACT] = tc.tRTP + tc.tRP;
                c[(int)CmdType.WR_AP, (int)CmdType.ACT] = tc.tCWL + tc.tBL + tc.tWR + tc.tRP;

                if (Config.mctrl.salp == SALP.MASA)
                {
                    c[(int)CmdType.SEL_SA, (int)CmdType.RD] = tc.tSCD;
                    c[(int)CmdType.SEL_SA, (int)CmdType.RD_AP] = tc.tSCD;
                    c[(int)CmdType.SEL_SA, (int)CmdType.WR] = tc.tSCD;
                    c[(int)CmdType.SEL_SA, (int)CmdType.WR_AP] = tc.tSCD;

                    c[(int)CmdType.RD_AP, (int)CmdType.ACT] = tc.tRTP + tc.tRP;
                    c[(int)CmdType.WR_AP, (int)CmdType.ACT] = tc.tCWL + tc.tBL + tc.tWR + tc.tRP;
                }
            }
        }

        private void init_sibling_constrs_array()
        {
            SiblingConstrsArray = new uint[(int)TimingNode.Level.MAX][,];
            for (int i = 0; i < ConstrsArray.Length; i++)
            {
                SiblingConstrsArray[i] = new uint[(int)CmdType.MAX, (int)CmdType.MAX];

                for (int j = 0; j < (int)CmdType.MAX; j++)
                    for (int k = 0; k < (int)CmdType.MAX; k++)
                        SiblingConstrsArray[i][j, k] = 1;
            }

            uint[,] c;

            /*** Channel ***/
            c = SiblingConstrsArray[(int)TimingNode.Level.CHANNEL];

            /*** Rank ***/
            c = SiblingConstrsArray[(int)TimingNode.Level.RANK];

            c[(int)CmdType.RD, (int)CmdType.RD] = tc.tBL + tc.tRTRS;
            c[(int)CmdType.RD, (int)CmdType.RD_AP] = tc.tBL + tc.tRTRS;
            c[(int)CmdType.RD_AP, (int)CmdType.RD] = tc.tBL + tc.tRTRS;
            c[(int)CmdType.RD_AP, (int)CmdType.RD_AP] = tc.tBL + tc.tRTRS;

            c[(int)CmdType.WR, (int)CmdType.WR] = tc.tBL + tc.tRTRS;
            c[(int)CmdType.WR, (int)CmdType.WR_AP] = tc.tBL + tc.tRTRS;
            c[(int)CmdType.WR_AP, (int)CmdType.WR] = tc.tBL + tc.tRTRS;
            c[(int)CmdType.WR_AP, (int)CmdType.WR_AP] = tc.tBL + tc.tRTRS;

            c[(int)CmdType.RD, (int)CmdType.WR] = tc.tCL + tc.tBL + tc.tRTRS - tc.tCWL;
            c[(int)CmdType.RD, (int)CmdType.WR_AP] = tc.tCL + tc.tBL + tc.tRTRS - tc.tCWL;
            c[(int)CmdType.RD_AP, (int)CmdType.WR] = tc.tCL + tc.tBL + tc.tRTRS - tc.tCWL;
            c[(int)CmdType.RD_AP, (int)CmdType.WR_AP] = tc.tCL + tc.tBL + tc.tRTRS - tc.tCWL;

            c[(int)CmdType.WR, (int)CmdType.RD] = tc.tCWL + tc.tBL + tc.tRTRS - tc.tCL;
            c[(int)CmdType.WR, (int)CmdType.RD_AP] = tc.tCWL + tc.tBL + tc.tRTRS - tc.tCL;
            c[(int)CmdType.WR_AP, (int)CmdType.RD] = tc.tCWL + tc.tBL + tc.tRTRS - tc.tCL;
            c[(int)CmdType.WR_AP, (int)CmdType.RD_AP] = tc.tCWL + tc.tBL + tc.tRTRS - tc.tCL;

            /*** Bank ***/
            c = SiblingConstrsArray[(int)TimingNode.Level.BANK];

            /*** Subarray ***/
            c = SiblingConstrsArray[(int)TimingNode.Level.SUBARRAY];

            if (Config.mctrl.salp == SALP.NONE)
            {
                // no timing constraints (subarray-oblivious)
            }
            else if (Config.mctrl.salp == SALP.SALP1)
            {
                // the following three constraints are needed only for "autoprecharge" column commands
                c[(int)CmdType.ACT, (int)CmdType.ACT] = tc.tRC - tc.tRP + 1;

                c[(int)CmdType.RD_AP, (int)CmdType.ACT] = tc.tRTP + 1;
                c[(int)CmdType.WR_AP, (int)CmdType.ACT] = tc.tCWL + tc.tBL + tc.tWR + 1;
            }
            else if (Config.mctrl.salp == SALP.SALP2)
            {
                c[(int)CmdType.ACT, (int)CmdType.ACT] = tc.tRCD + tc.tRA;

                c[(int)CmdType.RD, (int)CmdType.ACT] = tc.tRA;
                c[(int)CmdType.RD_AP, (int)CmdType.ACT] = tc.tRA;
                c[(int)CmdType.WR, (int)CmdType.ACT] = tc.tWA;
                c[(int)CmdType.WR_AP, (int)CmdType.ACT] = tc.tWA;
            }
            else if (Config.mctrl.salp == SALP.MASA)
            {
                c[(int)CmdType.RD, (int)CmdType.ACT] = tc.tRA;
                c[(int)CmdType.RD_AP, (int)CmdType.ACT] = tc.tRA;
                c[(int)CmdType.WR, (int)CmdType.ACT] = tc.tWA;
                c[(int)CmdType.WR_AP, (int)CmdType.ACT] = tc.tWA;

                c[(int)CmdType.RD, (int)CmdType.SEL_SA] = tc.tRA;
                c[(int)CmdType.RD_AP, (int)CmdType.SEL_SA] = tc.tRA;
                c[(int)CmdType.WR, (int)CmdType.SEL_SA] = tc.tWA;
                c[(int)CmdType.WR_AP, (int)CmdType.SEL_SA] = tc.tWA;

                // Piggyback (integrate) SEL_SA into RD. Easier to schedule, but this releases some pressure on command bus
                c[(int)CmdType.RD, (int)CmdType.RD] = tc.tRA;
                c[(int)CmdType.RD_AP, (int)CmdType.RD_AP] = tc.tRA;
                c[(int)CmdType.WR, (int)CmdType.WR] = tc.tWA;
                c[(int)CmdType.WR_AP, (int)CmdType.WR_AP] = tc.tWA;
            }
        }

        public bool Check(long cycles, CmdType c, MemAddr a)
        {
            uint[] addrArray = { a.cid, a.rid, a.bid, a.said };
            return Channel.Check(cycles, (int)c, addrArray);
        }

        public void Update(long cycles, CmdType c, MemAddr a, bool villaCache = false,
                            bool chargeCacheHit = false)
        {
            uint[] addrArray = { a.cid, a.rid, a.bid, a.said };
            Channel.Update(cycles, (int)c, addrArray, villaCache, chargeCacheHit);
        }

        public void update_rank_trfc(uint rfc)
        {
            ServiceArray[(int)CmdType.REF_RANK] = rfc;
            // Rank
            uint[,] c;
            c = ConstrsArray[(int)TimingNode.Level.RANK];
            c[(int)CmdType.REF_RANK, (int)CmdType.ACT] = rfc;
            c[(int)CmdType.REF_RANK, (int)CmdType.RD] = rfc;
            c[(int)CmdType.REF_RANK, (int)CmdType.RD_AP] = rfc;
            c[(int)CmdType.REF_RANK, (int)CmdType.WR] = rfc;
            c[(int)CmdType.REF_RANK, (int)CmdType.WR_AP] = rfc;
            c[(int)CmdType.REF_RANK, (int)CmdType.PRE_RANK] = rfc;
            c[(int)CmdType.REF_RANK, (int)CmdType.PRE_BANK] = rfc;
            c[(int)CmdType.REF_RANK, (int)CmdType.REF_BANK] = rfc;
            c[(int)CmdType.REF_RANK, (int)CmdType.PRE_RANK] = rfc;
            c[(int)CmdType.REF_RANK, (int)CmdType.REF_RANK] = rfc;
        }

        // method
    } //class
} // namespace
