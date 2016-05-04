using Ramulator.Mem;
using Ramulator.MemCtrl.Refresh;
using System;
using System.Collections.Generic;
using Ramulator.MemReq;
using Ramulator.Sim;

namespace Ramulator.MemCtrl
{
    public delegate void Callback(Req req);

    public class MemCtrl
    {
        // id
        public static uint Cmax;

        public uint cid;

        // state
        public long Cycles;

        // fanout
        public uint Rmax, Bmax, Smax;

        // _ddr3
        public DDR3DRAM.Timing tc;

        public uint ColMax;

        // dram timing & state-machine
        public DRAMTiming Dtiming;

        public DRAMState Dstate;
        public DDR3DRAM ddr3;

        // Extra timing
        public DDR3DRAM villa_ddr3;

        public DDR3DRAM.Timing villa_tc;
        public DRAMTiming VillaDtiming;

        // scheduler
        public MemSched.MemSched Rsched, Wsched;

        // refresh
        public RefreshPolicy Refresher;

        // round-robin to find bank refresh request
        private int _findBankRefBase;

        private bool _noRefresh;

        // request queues
        public List<Req>[,] Readqs, Writeqs, Refbankqs, Copyqs, Inflightqs;

        public List<Req>[] Refrankqs;

        // request queue size
        public int ReadqMax, WriteqMax;

        // bus queue
        public List<BusTransaction> BusQ;

        private static readonly uint BUS_TRANSACTIONS_MAX = 512;

        // copy complete queue -- to determine when a copy request is complete
        public List<BusTransaction> CopyCompleteQ;

        // writeback
        public bool Wbmode;

        public MemWBMode.MemWBMode Mwbmode;
        private long _tsStartWbmode = -1;
        private long _tsEndWbmode = -1;

        // stats
        public int Rload, Wload;

        public ulong[, ,] ShadowRowidPerProcrankbank;
        public uint[] RloadPerProc, WloadPerProc;
        public uint[, ,] RloadPerProcrankbank, WloadPerProcrankbank;

        // writeback mode stats
        private uint _rdsPerWbMode, _wbsPerWbMode;

        // Procs Handles
        public Proc.Proc[] ProcHandles;

        // MASA
        private readonly Cmd[,] _prevIssuedCmd;

        // Monitor the hit counts / access frequency
        public MemCacheMonitor MemCacheMon;

        // VILLA
        public DRAMCache[,] DramCachePerBank;

        public RBLA_Stats[,] DramCacheStats;
        public RBLA_Monitor RblaMonitor;

        // Copy
        private readonly bool _bFindCopyRequests;

        // ctor
        public MemCtrl(uint rmax, DDR3DRAM ddr3)
        {
            cid = Cmax;
            Cmax++;

            // states
            Rmax = rmax;
            Bmax = ddr3.BANK_MAX;
            Smax = ddr3.SUBARRAYS_PER_BANK;

            // DDR3
            tc = ddr3.timing;
            ColMax = ddr3.COL_MAX;
            this.ddr3 = ddr3;

            // Refresher
            select_refresh_policy(ddr3);

            // dram timing & state-machine
            uint[] fanoutArray = { 1, rmax, Bmax, Smax };
            if (!Config.mctrl.villa_cache)
            {
                Dtiming = new DRAMTiming(tc, cid, fanoutArray);
            }
            else
            {
                // VILLA - caching
                villa_ddr3 = new DDR3DRAM(Config.mem.ddr3_type,
                        Config.mem.clock_factor, Config.mem.tRTRS, Config.mem.tWR,
                        Config.mem.tWTR, Config.mem.tBL, Config.mem.bank_max,
                        Config.mem.subarray_max, Config.mem.col_max,
                        Config.mem.tRA, Config.mem.tWA, Config.mem.tREFI,
                        Config.mem.tRFC, Config.mem.tRP, Config.mem.tRCD);
                villa_tc = villa_ddr3.timing;
                villa_tc.tRCD = (uint)(villa_tc.tRCD * Config.mctrl.villa_tRCD_frac);
                villa_tc.tRAS = (uint)(villa_tc.tRAS * Config.mctrl.villa_tRAS_frac);
                villa_tc.tRP = (uint)(villa_tc.tRP * Config.mctrl.villa_tRP_frac);
                villa_tc.tRC = villa_tc.tRAS + villa_tc.tRP;
                villa_tc.update_lisa_clone_as_cache(Config.mem.subarray_max / Config.mctrl.num_villa_sa - 1); // Number of VILLA SAs
                tc.update_lisa_clone_as_cache(Config.mem.subarray_max / Config.mctrl.num_villa_sa - 1); // Number of VILLA SAs

                if (Config.mctrl.villa_ideal)
                {
                    tc.tLISA_INTER_SA_COPY = 0;
                    villa_tc.tLISA_INTER_SA_COPY = 0;
                }

                VillaDtiming = new DRAMTiming(villa_tc, cid, fanoutArray);
                Dtiming = new DRAMTiming(tc, cid, fanoutArray, VillaDtiming);

                // Villa cache setup
                uint cachelineSize = (uint)Config.proc.block_size;
                uint rowSize = cachelineSize * villa_ddr3.COL_MAX;
                uint cacheSize = Config.mctrl.villa_fast_sa_num_rows * rowSize;

                // Basically one set with muliple ways to get a LRU chain
                DramCachePerBank = new DRAMCache[rmax, Bmax];
                DramCacheStats = new RBLA_Stats[rmax, Bmax];
                uint rblaSize = Config.mctrl.rbla_assoc * Config.mctrl.rbla_sets * rowSize;
                for (uint dr = 0; dr < rmax; dr++)
                    for (uint br = 0; br < Bmax; br++)
                    {
                        DramCachePerBank[dr, br] = new DRAMCache(cacheSize, Config.mctrl.villa_fast_sa_num_rows, rowSize, 0, cid, dr, br);
                        DramCacheStats[dr, br] = new RBLA_Stats(rblaSize, Config.mctrl.rbla_assoc, rowSize, 0, cid, dr, br, tc);
                    }
                if (Config.mctrl.villa_cache_method == VILLA_HOT.RBLA || Config.mctrl.villa_cache_method == VILLA_HOT.EPOCH)
                    RblaMonitor = new RBLA_Monitor(this, villa_tc, tc);
                if (Config.mctrl.villa_cache_method == VILLA_HOT.EPOCH)
                    MemCacheMon = new MemCacheMonitor(this);
            }
            Dstate = new DRAMState(cid, fanoutArray, this);

            // queue size
            ReadqMax = Config.mctrl.readq_max_per_chan;
            WriteqMax = Config.mctrl.writeq_max_per_chan;

            // queues
            Readqs = new List<Req>[rmax, Bmax];
            Writeqs = new List<Req>[rmax, Bmax];
            Refrankqs = new List<Req>[rmax];
            Refbankqs = new List<Req>[rmax, Bmax];
            Copyqs = new List<Req>[rmax, Bmax];
            Inflightqs = new List<Req>[rmax, Bmax];
            _prevIssuedCmd = new Cmd[rmax, Bmax];

            for (int r = 0; r < rmax; r++)
            {
                Refrankqs[r] = new List<Req>();
                for (int b = 0; b < Bmax; b++)
                {
                    Readqs[r, b] = new List<Req>(ReadqMax);
                    Writeqs[r, b] = new List<Req>(WriteqMax);
                    Refbankqs[r, b] = new List<Req>();
                    Copyqs[r, b] = new List<Req>(Config.mctrl.copyq_max);
                    Inflightqs[r, b] = new List<Req>(ReadqMax);
                    _prevIssuedCmd[r, b] = null;
                }
            }

            BusQ = new List<BusTransaction>((int)BUS_TRANSACTIONS_MAX);
            CopyCompleteQ = new List<BusTransaction>((int)BUS_TRANSACTIONS_MAX);
            ProcHandles = new Proc.Proc[Config.N];

            // stats
            RloadPerProc = new uint[Config.N];
            RloadPerProcrankbank = new uint[Config.N, rmax, Bmax];
            ShadowRowidPerProcrankbank = new ulong[Config.N, rmax, Bmax];
            WloadPerProc = new uint[Config.N];
            WloadPerProcrankbank = new uint[Config.N, rmax, Bmax];

            _bFindCopyRequests = Config.proc.b_read_rc_traces || Config.mctrl.villa_cache;
        }

        private void select_refresh_policy(DDR3DRAM _ddr3)
        {
            _findBankRefBase = 0;

            // Refresh at the bank level
            if (Config.mctrl.refresh_policy == RefreshEnum.AUTO_BANK)
                Config.mctrl.b_refresh_bank = true;

            ulong _tREFI = (Config.mctrl.b_refresh_bank) ? _ddr3.timing.tREFIpb : _ddr3.timing.tREFI;
            switch (Config.mctrl.refresh_policy)
            {
                case RefreshEnum.NONE:
                    Refresher = new RefreshPolicy(this, _tREFI);
                    _noRefresh = true;
                    break;

                case RefreshEnum.AUTO:
                case RefreshEnum.AUTO_BANK:
                    Refresher = new AutoRefresh(this, _tREFI);
                    break;

                default:
                    throw new Exception("Undefined refresher " + Config.mctrl.refresh_policy);
            }
        }

        // Action to perform in each clock cycle at the memory controller
        public void Tick()
        {
            // Must be the very first thing that's done
            Cycles++;
            Rsched.Tick();
            Wsched.Tick();
            Mwbmode.Tick(cid);
            Refresher.Tick();

            // Clear cache monitor
            if (MemCacheMon != null && Cycles % Config.mctrl.cache_mon_epoch == 0)
                MemCacheMon.end_epoch();

            // VILLA Cache -- clean up the history
            if (Config.mctrl.villa_cache && (Cycles % Config.mctrl.rbla_epoch_clean_threshold == 0))
            {
                for (uint dr = 0; dr < Rmax; dr++)
                    for (uint br = 0; br < Bmax; br++)
                        DramCacheStats[dr, br].clean_history();
                if (RblaMonitor != null)
                    RblaMonitor.calc_benefit();
            }

            // Stats: load
            for (int p = 0; p < Config.N; p++)
            {
                if (RloadPerProc[p] > 0)
                    Stat.mctrls[cid].rbinaryloadtick_per_proc[p].collect();
                Stat.mctrls[cid].rloadtick_per_proc[p].collect(RloadPerProc[p]);

                if (WloadPerProc[p] > 0)
                    Stat.mctrls[cid].wbinaryloadtick_per_proc[p].collect();
                Stat.mctrls[cid].wloadtick_per_proc[p].collect(WloadPerProc[p]);
            }
            // Stats: bank
            for (uint rid = 0; rid < Config.mem.rank_max; rid++)
            {
                for (uint bid = 0; bid < Config.mem.bank_max; bid++)
                {
                    BankStat bankStat = Stat.banks[cid, rid, bid];
                    bankStat.avg_open_subarrays.collect(Dstate.get_num_open_subarray(rid, bid));
                }
            }

            // Clock factor -- should this be here? Why couldn't I issue request at CPU freq?
            if (Cycles % Config.mem.clock_factor != 0)
                return;

            // Writeback mode
            Wbmode = update_wbmode();

            // Finish processing requests that have been issued
            ServeCompletedRequest();

            // Find request to issue
            Req bestReq = find_best_req();
            if (bestReq == null)
                return;

            // Translate the request to DRAM commands
            Cmd cmd = Dstate.Crack(bestReq);
            Dbg.Assert(Dtiming.Check(Cycles, cmd.Type, cmd.Addr));
            _prevIssuedCmd[cmd.Addr.rid, cmd.Addr.bid] = cmd;

            // Probe the VILLA cache
            if (Config.mctrl.villa_cache)
            {
                DRAMCache drc = DramCachePerBank[cmd.Addr.rid, cmd.Addr.bid];
                if (!drc.in_cache(cmd.Addr.rowid))
                {
                    Req cReq = CacheVilla(cmd);
                    if (cReq != null)
                    {
                        bestReq = cReq;
                        cmd = Dstate.Crack(bestReq);
                        Dbg.Assert(Dtiming.Check(Cycles, cmd.Type, cmd.Addr));
                        _prevIssuedCmd[cmd.Addr.rid, cmd.Addr.bid] = cmd;
                    }
                }
            }

            // GUI debugger tool: Draw commands on the timeline
            if (Program.gui != null)
                Program.gui.tgfx.DrawCmd(cmd, Cycles);

            // Issue req
            if (cmd.is_column() || cmd.is_refresh() || cmd.is_copy())
                issue_req(bestReq, cmd);

            // Issue cmd
            issue_cmd(cmd);

            // alert writeback mode
            if (cmd.is_write())
                Mwbmode.issued_write_cmd(cmd);

            // Recycle refresh request
            if (cmd.is_refresh())
                RequestPool.Enpool(bestReq);
        }

        // Retire those memory requests that have completed
        private void ServeCompletedRequest()
        {
            // Serve completed request
            if (BusQ.Count > 0 && BusQ[0].ts <= Cycles)
            {
                Dbg.AssertPrint(BusQ[0].ts == Cycles, "Bus tx should be precise.");
                var addr = BusQ[0].Addr;
                BusQ.RemoveAt(0);

                List<Req> inflightq = Inflightqs[addr.rid, addr.bid];
                List<Req> matches = inflightq.FindAll(r => r.Addr == addr);
                Dbg.Assert(matches.Count == 1);
                Req req = matches[0];

                // Normal requests
                inflightq.Remove(req);

                // Send back to CPU
                dequeue_req(req);
            }

            // Serve completed copy request
            if (CopyCompleteQ.Count > 0)
                serve_completed_copy_req();
        }

        // Retire those copy requests that have completed
        private void serve_completed_copy_req()
        {
            // Retire out-of-order: find the first one that completes under time to retire
            int gold = -1;
            for (int i = 0; i < CopyCompleteQ.Count; i++)
                if (CopyCompleteQ[i].ts <= Cycles)
                {
                    gold = i;
                    break;
                }
            if (gold == -1)
                return;

            MemAddr addr = CopyCompleteQ[gold].Addr;
            CopyCompleteQ.RemoveAt(gold);

            List<Req> inflightq = Inflightqs[addr.rid, addr.bid];
            List<Req> matches = inflightq.FindAll(r => (r.Addr == addr) && (r.Type == ReqType.COPY));
            Dbg.Assert(matches.Count == 1);
            Req req = matches[0];
            inflightq.Remove(req);

            // Send back to CPU or simply retire it for the in-DRAM cache
            if (req.Callback == null)
            {
                DRAMCache drc = DramCachePerBank[addr.rid, addr.bid];
                drc.cache_add(addr.rowid, ReqType.READ, req.Pid);
                RequestPool.Enpool(req);
            }
            else
                dequeue_req(req);
        }

        // Checks whether a request hits in its row-buffer
        public bool is_row_hit(Req req)
        {
            Cmd cmd = Dstate.Crack(req);
            switch (cmd.Type)
            {
                case CmdType.SEL_SA:
                case CmdType.RD:
                case CmdType.WR:
                    return true;
            }
            return false;
        }

        /**
         * Various methods to find different types of schedulable memory requests besides read and write.
         * Some other types of request used are refresh and copy.
         **/

        private bool find_rank_ref(ref Req refReqOut)
        {
            bool bFailTiming = false;

            // refresh (rank)
            for (int r = 0; r < Rmax; r++)
            {
                List<Req> q = Refrankqs[r];
                if (q.Count > 0)
                {
                    // Without elastic refresh or any other postponing method, there should be only 1 ref req per rank
                    Dbg.AssertPrint(q.Count == 1, "There should be 1 ref req per rank for AutoRef");

                    Req refReq = q[0];
                    Dbg.AssertPrint(refReq.Type == ReqType.REFRESH, "Refresh request needs to be at the rank-level.");
                    Cmd refCmd = Dstate.Crack(refReq);

                    if (Dtiming.Check(Cycles, refCmd.Type, refCmd.Addr))
                    {
                        refReqOut = refReq;
                        return true;
                    }
                    else
                    {
                        refReqOut = null;
                        bFailTiming = true;
                    }
                }
            }

            return bFailTiming;
        }

        private bool find_bank_ref(ref Req refReqOut)
        {
            Dbg.AssertPrint(Config.mctrl.b_refresh_bank, "Should only refresh bank");
            bool bFailTiming = false;

            // refresh (bank)
            for (int r = 0; r < Rmax; r++)
            {
                for (int bOffset = 0; bOffset < Bmax; bOffset++)
                {
                    // Round robin to find the bank to be refreshed
                    int b = (Config.mctrl.round_robin_ref) ? (_findBankRefBase + bOffset) % (int)Bmax : bOffset;

                    List<Req> q = Refbankqs[r, b];
                    Dbg.Assert(q.Count <= Config.mctrl.max_delay_ref_counts);
                    if (q.Count > 0)
                    {
                        Req refReq = q[0];
                        Dbg.AssertPrint(refReq.Type == ReqType.REFRESH_BANK, "Refresh request needs to be at the bank-level.");
                        Cmd refCmd = Dstate.Crack(refReq);

                        if (Dtiming.Check(Cycles, refCmd.Type, refCmd.Addr))
                        {
                            refReqOut = refReq;
                            return true;
                        }
                        else
                        {
                            refReqOut = null;
                            bFailTiming = true;
                        }
                    }
                }
                _findBankRefBase++;
                _findBankRefBase %= (int)Bmax;
            }

            return bFailTiming;
        }

        private bool find_bank_copy(ref Req cpReqOut)
        {
            bool bFailTiming = false;

            for (int r = 0; r < Rmax; r++)
            {
                for (int bOffset = 0; bOffset < Bmax; bOffset++)
                {
                    int b = bOffset;
                    List<Req> q = Copyqs[r, b];
                    if (q.Count > 0)
                    {
                        Req cpReq = q[0];
                        Dbg.AssertPrint(cpReq.Type == ReqType.COPY, "This needs to be a copy request!");
                        Cmd cpCmd = Dstate.Crack(cpReq);

                        if (Dtiming.Check(Cycles, cpCmd.Type, cpCmd.Addr))
                        {
                            cpReqOut = cpReq;
                            return true;
                        }
                        else
                        {
                            cpReqOut = null;
                            bFailTiming = true;
                        }
                    }
                }
            }
            return bFailTiming;
        }

        public Req find_best_req()
        {
            Req bestReq = null;

            // Process refresh requests first
            bool enterRefresh = false;
            if (!_noRefresh)
            {
                enterRefresh = !Config.mctrl.b_refresh_bank ? find_rank_ref(ref bestReq) : find_bank_ref(ref bestReq);

                // Block the entire rank when the refresh command is at rank-level
                // to prevent further commands from occupying the rank
                if (enterRefresh && !Config.mctrl.b_refresh_bank)
                    return bestReq;
                // Per-bank refresh: even if there is a pending per-bank refresh command,
                // we do not block the other non-refresh requests if the pending refresh is not ready
                if (enterRefresh && Config.mctrl.b_refresh_bank && (bestReq != null))
                    return bestReq;
            }

            // Process copy request first
            if (_bFindCopyRequests && find_bank_copy(ref bestReq))
                return bestReq;

            if (bestReq != null)
                return bestReq;

            // Statistic collection
            int countReq = 0;

            // Select a read/write request
            for (int r = 0; r < Rmax; r++)
            {
                for (int b = 0; b < Bmax; b++)
                {
                    // Check if there is a pending REFpb request that just got dynamically inserted during writeback mode
                    if (Wbmode && Refbankqs[r, b].Count > 0)
                        continue;
                    // Only issue requests to banks that don't have any scheduled bank-level refresh
                    if (enterRefresh && Refbankqs[r, b].Count > 0)
                    {
                        Dbg.AssertPrint(Config.mctrl.b_refresh_bank, "Non-blocking requests issued during refresh can only done at bank level.");
                        continue;
                    }
                    List<Req> q;
                    Req req = null;
                    // Read
                    if (!Wbmode)
                    {
                        q = Readqs[r, b];
                        // MASA-specific request finder as the current scheduler is not subarray-aware
                        if (Config.mctrl.salp == SALP.MASA)
                            masa_find_req(r, b, ref q, ref req);
                        // Select request based on the specified scheduler
                        if (req == null)
                            req = Rsched.find_best_req(q);
                    }
                    else
                    {
                        q = Writeqs[r, b];
                        req = Wsched.find_best_req(q);
                    }
                    if (req == null)
                        continue;

                    // Check timings
                    Cmd cmd = Dstate.Crack(req);
                    if (!Dtiming.Check(Cycles, cmd.Type, cmd.Addr))
                        continue;

                    countReq++;
                    if (bestReq == null)
                    {
                        bestReq = req;
                        continue;
                    }
                    bestReq = __better_req(bestReq, req);
                }
            }
            // Stall on refresh stats collection
            if (bestReq == null && countReq > 0 && !_noRefresh)
                Stat.mctrls[cid].stall_on_refresh.collect();

            return bestReq;
        }

        // MASA: use a queue to store all requests that can be issued now. The reason to use this q
        // is that a request is only removed from the request_queue when its command is a column-type
        // or a refresh. Without this, a request that does not pass the timing check can keep on
        // getting selected by the memory scheduler when others are avaiable to be issued given that
        // the timing check occurs *after* the scheduler decides which one to pick.
        private void masa_find_req(int r, int b, ref List<Req> q, ref Req req)
        {
            q = salp_create_valid_queue(q);

            // Find the READ request that should go after its SA_SEL or ACT has been issued
            Cmd prevCmd = _prevIssuedCmd[r, b];
            if (prevCmd != null && (prevCmd.Type == CmdType.SEL_SA || prevCmd.Type == CmdType.ACT))
            {
                foreach (Req masaReq in q)
                {
                    Cmd tmpCmd = Dstate.Crack(masaReq);
                    if (masaReq.Addr.said == prevCmd.Addr.said &&
                        masaReq.Addr.rowid == prevCmd.Addr.rowid &&
                        masaReq.Addr.colid == prevCmd.Addr.colid &&
                        tmpCmd.is_column())
                    {
                        req = masaReq;
                        break;
                    }
                }
            }
        }

        private List<Req> salp_create_valid_queue(List<Req> q)
        {
            // Create a temporary valid request queue
            List<Req> validQ = new List<Req>();
            foreach (Req req in q)
            {
                Cmd cmd = Dstate.Crack(req);
                Cmd prevCmd = _prevIssuedCmd[req.Addr.rid, req.Addr.bid];
                if (prevCmd != null && (prevCmd.Type == CmdType.ACT || prevCmd.Type == CmdType.SEL_SA) && cmd.Type == CmdType.SEL_SA &&
                    Config.mctrl.no_selsa_preemption)
                    continue;

                if (Dtiming.Check(Cycles, cmd.Type, cmd.Addr))
                    validQ.Add(req);
            }

            return validQ;
        }

        private Req __better_req(Req req1, Req req2)
        {
            bool isWr1 = req1.Type == ReqType.WRITE;
            bool isWr2 = req2.Type == ReqType.WRITE;

            if (isWr1 && isWr2)
            {
                return Wsched.better_req(req1, req2);
            }

            if (isWr1 ^ isWr2)
            {
                if (isWr1) return req1;
                else return req2;
            }

            //two reads
            return Rsched.better_req(req1, req2);
        }

        private void issue_req(Req req, Cmd cmd)
        {
            MemAddr addr = req.Addr;

            // remove request from waiting queue...
            List<Req> q = get_q(req);
            Dbg.Assert(q.Contains(req));
            q.Remove(req);

            // refresh requests bail
            if (req.Type == ReqType.REFRESH || req.Type == ReqType.REFRESH_BANK)
                return;
            Dbg.Assert(req.Type == ReqType.READ || req.Type == ReqType.WRITE || req.Type == ReqType.COPY);

            // ...and add it to inflight queue
            List<Req> inflightQ = get_inflightq(req);
            Dbg.Assert(inflightQ.Count < inflightQ.Capacity);
            inflightQ.Add(req);

            // alert scheduler
            Rsched.issue_req(req);
            Wsched.issue_req(req);

            // update shadow row-buffer (needs to come *after* alerting scheduler)
            ShadowRowidPerProcrankbank[req.Pid, req.Addr.rid, req.Addr.bid] = req.Addr.rowid;

            if (req.Type == ReqType.COPY)
            {
                // reserve bus
                Dbg.Assert(CopyCompleteQ.Count < CopyCompleteQ.Capacity);
                long ts = Cycles;
                long copyLat = 0;
                if (cmd.Type == CmdType.LINKS_INTER_SA_COPY)
                    copyLat = tc.tLISA_INTER_SA_COPY;
                else if (cmd.Type == CmdType.ROWCLONE_INTER_SA_COPY)
                    copyLat = tc.tRC_INTER_SA_COPY;
                else if (cmd.Type == CmdType.ROWCLONE_INTER_BANK_COPY)
                    copyLat = tc.tRC_INTER_BANK_COPY;
                else if (cmd.Type == CmdType.BASE_INTER_SA_COPY)
                    copyLat = tc.tNAIVE_BLOCK_INTER_SA_COPY;
                else if (cmd.Type == CmdType.ROWCLONE_INTRA_SA_COPY)
                    copyLat = tc.tRC_INTRA_SA_COPY;
                ts += copyLat;

                BusTransaction trans = new BusTransaction(addr, ts);
                CopyCompleteQ.Add(trans);
            }
            else
            {
                // reserve bus
                Dbg.Assert(BusQ.Count < BusQ.Capacity);
                long ts = Cycles;
                if (req.Type == ReqType.READ) ts += tc.tCL + tc.tBL;
                else ts += tc.tCWL + tc.tBL;

                BusTransaction trans = new BusTransaction(addr, ts);
                if (BusQ.Count > 0)
                {
                    //check for bus conflict
                    BusTransaction lastTrans = BusQ[BusQ.Count - 1];
                    Dbg.Assert(trans.ts - lastTrans.ts >= tc.tBL);
                }
                BusQ.Add(trans);
            }

            // stats: proc & bank
            if (!req.CpyGenReq)
                collect_req_stats(req);
        }

        private void collect_req_stats(Req req)
        {
            // Cache monitor
            if (MemCacheMon != null)
                MemCacheMon.record_addr_hit(req);

            ProcStat pstat = Stat.procs[req.Pid];
            BankStat bstat = Stat.banks[req.Addr.cid, req.Addr.rid, req.Addr.bid];

            bstat.access.collect();
            if (req.RequiredActivate)
            {
                // row-buffer miss
                bstat.row_miss.collect();
                bstat.row_miss_perproc[req.Pid].collect();

                if (req.Type == ReqType.READ)
                {
                    pstat.row_hit_rate_read.collect(0);
                    pstat.row_miss_read.collect();
                }
                else if (req.Type == ReqType.WRITE)
                {
                    pstat.row_hit_rate_write.collect(0);
                    pstat.row_miss_write.collect();
                }
            }
            else
            {
                // row-buffer hit
                bstat.row_hit.collect();
                bstat.row_hit_perproc[req.Pid].collect();

                if (req.Type == ReqType.READ)
                {
                    pstat.row_hit_rate_read.collect(1);
                    pstat.row_hit_read.collect();
                }
                else if (req.Type == ReqType.WRITE)
                {
                    pstat.row_hit_rate_write.collect(1);
                    pstat.row_hit_write.collect();
                }
            }

            if (req.Type == ReqType.READ)
            {
                int rdQLat = (int)(Cycles - req.TsArrival);
                pstat.read_queue_latency_perproc.collect(rdQLat);
                Stat.mctrls[cid].read_queue_latency_perchan.collect(rdQLat);
            }
        }

        private void issue_cmd(Cmd cmd)
        {
            // Debug -- dump all the commands into a file
            if (Program.debug_cmd_dump_file != null)
                Program.debug_cmd_dump_file.WriteLine("({0} -- {1});", Cycles, cmd.to_str());

            Req req = cmd.Req;
            cmd_issue_autoprecharge(cmd, req);

            // VILLA cache
            bool villaCacheHit = false;
            if (Config.mctrl.villa_cache && (cmd.Req.Type == ReqType.READ || cmd.Req.Type == ReqType.WRITE))
            {
                DRAMCache drc = DramCachePerBank[cmd.Addr.rid, cmd.Addr.bid];
                if (drc.is_cache_hit(cmd.Addr.rowid, cmd.Req.Type))
                {
                    villaCacheHit = true;
                    if (RblaMonitor != null)
                        RblaMonitor.Hit(cmd);
                }
            }

            // update dram timing and state
            Dtiming.Update(Cycles, cmd.Type, cmd.Addr, villaCacheHit);
            Dstate.Update(cmd.Type, cmd.Addr, Cycles);

            // VILLA cache
            if (Config.mctrl.villa_cache && Config.mctrl.villa_cache_method == VILLA_HOT.RBLA)
            {
                RBLA_Stats rbla = DramCacheStats[cmd.Addr.rid, cmd.Addr.bid];
                if (rbla.in_cache(cmd.Addr.rowid))
                    rbla.udpate_cache(cmd.Addr.rowid, cmd);
                else
                    rbla.cache_add(cmd.Addr.rowid, cmd);
            }

            // Done with the state update. Get some stats then.
            if (!req.CpyGenReq)
                collect_cmd_stats(cmd, req);
        }

        private Req CacheVilla(Cmd cmd)
        {
            if (cmd.Req.Type == ReqType.READ || cmd.Req.Type == ReqType.WRITE)
            {
                DRAMCache drc = DramCachePerBank[cmd.Addr.rid, cmd.Addr.bid];
                bool toCache = false;

                switch (Config.mctrl.villa_cache_method)
                {
                    case VILLA_HOT.ACT:
                        toCache = cmd.Type == CmdType.ACT;
                        break;

                    case VILLA_HOT.PRE:
                        toCache = cmd.Type == CmdType.PRE_BANK;
                        break;

                    case VILLA_HOT.EPOCH:
                        if (cmd.Type != CmdType.ACT)
                            break;
                        toCache = MemCacheMon.is_req_hot(cmd.Req);
                        break;

                    case VILLA_HOT.RBLA:
                        if (cmd.Type != CmdType.ACT)
                            break;
                        toCache = DramCacheStats[cmd.Addr.rid, cmd.Addr.bid].to_cache(cmd.Addr.rowid);
                        break;

                    default:
                        throw new Exception("Unknown methods.");
                }

                // Cache on precharge or act if it's not there yet
                if (toCache && !drc.in_cache(cmd.Addr.rowid))
                {
                    //Prepare for caching
                    if (RblaMonitor != null)
                        RblaMonitor.Migrate();

                    // add copy request to queue
                    List<Req> cq = Copyqs[cmd.Addr.rid, cmd.Addr.bid];
                    Req cacheReq = RequestPool.Depool();
                    cacheReq.Type = ReqType.COPY;
                    cacheReq.TsArrival = Cycles;
                    cacheReq.Pid = cmd.Req.Pid;

                    // Set up the refresh target address
                    MemAddr cAddr = new MemAddr(cmd.Addr);
                    cacheReq.Addr = cAddr;
                    cacheReq.Callback = null;

                    cq.Add(cacheReq);
                    return cacheReq;
                }
            }
            return null;
        }

        // Collect some stats on what commands have been issued
        private void collect_cmd_stats(Cmd cmd, Req req)
        {
            // stats: row-buffer
            if (cmd.Type == CmdType.ACT)
            {
                Dbg.Assert(req.Type == ReqType.READ || req.Type == ReqType.WRITE);
                req.RequiredActivate = true;
            }

            // stats: writeback mode
            if (Wbmode)
            {
                if (cmd.is_read())
                    _rdsPerWbMode++;
                else if (cmd.is_write())
                    _wbsPerWbMode++;
            }

            // stats: bank
            BankStat bank;
            BankStat[] rank = new BankStat[Bmax];
            for (int bid = 0; bid < Bmax; bid++)
            {
                rank[bid] = Stat.banks[cmd.Addr.cid, cmd.Addr.rid, bid];
            }

            // stats: bank utilization
            uint work = Dtiming.ServiceArray[(int)cmd.Type];
            if (!cmd.is_rank())
            {
                // bank-level commands
                bank = Stat.banks[cmd.Addr.cid, cmd.Addr.rid, cmd.Addr.bid];
                bank.utilization.collect(work);
            }
            else
            {
                // rank-level commands
                foreach (BankStat b in rank)
                    b.utilization.collect(work);
            }

            // stats: bank commands
            bank = Stat.banks[cmd.Addr.cid, cmd.Addr.rid, cmd.Addr.bid];
            if (!cmd.is_rank())
            {
                switch (cmd.Type)
                {
                    case CmdType.ACT: bank.cmd_act.collect(); break;
                    case CmdType.SEL_SA: bank.cmd_sel_sa.collect(); break;
                    case CmdType.PRE_BANK: bank.cmd_pre_bank.collect(); break;
                    case CmdType.PRE_SA: bank.cmd_pre_sa.collect(); break;
                    case CmdType.RD: bank.cmd_rd.collect(); break;
                    case CmdType.WR: bank.cmd_wr.collect(); break;
                    case CmdType.RD_AP: bank.cmd_rd_ap.collect(); break;
                    case CmdType.WR_AP: bank.cmd_wr_ap.collect(); break;
                    case CmdType.REF_BANK: bank.cmd_ref_bank.collect(); break;
                    case CmdType.ROWCLONE_INTRA_SA_COPY: bank.cmd_rc_intra_sa.collect(); break;
                    case CmdType.ROWCLONE_INTER_SA_COPY: bank.cmd_rc_inter_sa.collect(); break;
                    case CmdType.ROWCLONE_INTER_BANK_COPY: bank.cmd_rc_inter_bank.collect(); break;
                    case CmdType.LINKS_INTER_SA_COPY: bank.cmd_links_inter_sa.collect(); break;
                    case CmdType.BASE_INTER_SA_COPY: bank.cmd_base_inter_sa.collect(); break;
                    default: throw new Exception("DRAM: Invalid Cmd.");
                }
            }
            else
            {
                switch (cmd.Type)
                {
                    case CmdType.PRE_RANK:
                        foreach (BankStat b in rank) b.cmd_pre_rank.collect();
                        break;

                    case CmdType.REF_RANK:
                        foreach (BankStat b in rank) b.cmd_ref_rank.collect();
                        break;

                    default: throw new Exception("DRAM: Invalid Cmd.");
                }
            }

            // stats: bus
            BusStat bus = Stat.busses[cmd.Addr.cid];
            if (cmd.is_column())
            {
                bus.access.collect();
                bus.utilization.collect(tc.tBL);
            }
        }

        // Autoprecharge: precharge a bank after a read/write
        // if there are no other pending requests to the same row
        private void cmd_issue_autoprecharge(Cmd cmd, Req req)
        {
            if (cmd.is_column() && !Config.mctrl.open_row_policy)
            {
                List<Req> q = get_q(req);
                List<Req> hits = q.FindAll(r => r.Addr.rowid == req.Addr.rowid);
                if (hits.Count == 0)
                {
                    if (cmd.Type == CmdType.RD)
                        cmd.Type = CmdType.RD_AP;
                    else if (cmd.Type == CmdType.WR)
                        cmd.Type = CmdType.WR_AP;
                }
            }
        }

        public bool is_q_full(int pid, ReqType rw, uint rid, uint bid)
        {
            // read queue
            if (rw == ReqType.READ)
            {
                if (Rload < ReadqMax) return false;
                else return true;
            }

            // write queue
            if (Wload < WriteqMax) return false;
            else return true;
        }

        public List<Req> get_q(Req req)
        {
            if (req.Type == ReqType.REFRESH)
                return Refrankqs[req.Addr.rid];
            else if (req.Type == ReqType.REFRESH_BANK)
                return Refbankqs[req.Addr.rid, req.Addr.bid];
            else if (req.Type == ReqType.COPY)
                return Copyqs[req.Addr.rid, req.Addr.bid];

            List<Req>[,] rwQs = (req.Type == ReqType.READ ? Readqs : Writeqs);
            List<Req> q = rwQs[req.Addr.rid, req.Addr.bid];
            return q;
        }

        public List<Req> get_q_with_id(uint rank, uint bank)
        {
            List<Req>[,] rwQs = (Wbmode) ? Writeqs : Readqs;
            List<Req> q = rwQs[rank, bank];
            return q;
        }

        public List<Req> get_active_q(Req req)
        {
            return get_active_q(req.Addr.rid, req.Addr.bid);
        }

        public List<Req> get_active_q(uint rid, uint bid)
        {
            List<Req>[,] rwQs = (Wbmode ? Readqs : Writeqs);
            List<Req> q = rwQs[rid, bid];
            return q;
        }

        public List<Req> get_inflightq(Req req)
        {
            List<Req> q = Inflightqs[req.Addr.rid, req.Addr.bid];
            return q;
        }

        public void enqueue_req(Req req)
        {
            Dbg.Assert(req.Type == ReqType.READ || req.Type == ReqType.WRITE || req.Type == ReqType.COPY);

            // timestamp
            req.TsArrival = Cycles;

            // check if writeback hit
            List<Req> q = get_q(req);
            MemAddr addr = req.Addr;
            if (req.Type == ReqType.READ)
            {
                List<Req> wq = Writeqs[addr.rid, addr.bid];

                int idx = wq.FindIndex(w => w.BlockAddr == req.BlockAddr);
                if (idx != -1)
                {
                    //writeback hit
                    Sim.Sim.Xbar.Enqueue(req);
                    Stat.procs[req.Pid].wb_hit.collect();
                    return;
                }
            }

            // enqueue proper
            Dbg.Assert(q.Count < q.Capacity);
            q.Add(req);

            // alert scheduler (does nothing for now)
            Rsched.enqueue_req(req);
            Wsched.enqueue_req(req);

            // stats
            if (req.Type == ReqType.READ)
            {
                Rload++;
                RloadPerProc[req.Pid]++;
                RloadPerProcrankbank[req.Pid, req.Addr.rid, req.Addr.bid]++;
            }
            else if (req.Type == ReqType.WRITE)
            {
                Wload++;
                WloadPerProc[req.Pid]++;
                WloadPerProcrankbank[req.Pid, req.Addr.rid, req.Addr.bid]++;
            }
        }

        public void dequeue_req(Req req)
        {
            Dbg.Assert(req.Type == ReqType.READ || req.Type == ReqType.WRITE || req.Type == ReqType.COPY);

            // timestamp
            req.TsDeparture = Cycles;
            Dbg.Assert(req.TsDeparture - req.TsArrival > 0);

            // alert scheduler
            Rsched.dequeue_req(req);
            Wsched.dequeue_req(req);

            // stats: load
            if (req.Type == ReqType.READ)
            {
                Rload--;
                RloadPerProc[req.Pid]--;
                RloadPerProcrankbank[req.Pid, req.Addr.rid, req.Addr.bid]--;
                Dbg.Assert(Rload >= 0);
            }
            else if (req.Type == ReqType.WRITE)
            {
                Wload--;
                WloadPerProc[req.Pid]--;
                WloadPerProcrankbank[req.Pid, req.Addr.rid, req.Addr.bid]--;
                Dbg.Assert(Wload >= 0);
            }

            // dequeue proper
            if (req.Type == ReqType.READ)
            {
                // XBAR
                Sim.Sim.Xbar.Enqueue(req);
            }
            else
            {
                req.Latency = (int)(req.TsDeparture - req.TsArrival);
                Callback cb = req.Callback;
                cb(req);
            }
        }

        public bool update_wbmode()
        {
            bool prevWbMode = Wbmode;
            Wbmode = Mwbmode.is_wb_mode(cid);

            if (Wbmode)
            {
                uint clockFactor = Config.mem.clock_factor;
                Stat.mctrls[cid].wbmode_fraction.collect(clockFactor);
            }

            // stats
            MemCtrlStat mctrl = Stat.mctrls[cid];

            if (prevWbMode == false && Wbmode)
            {
                // enter writeback mode
                _tsStartWbmode = Cycles;
                if (_tsEndWbmode != -1)
                    mctrl.wbmode_distance.collect((int)(_tsStartWbmode - _tsEndWbmode));
            }
            else if (prevWbMode && Wbmode == false)
            {
                // exit writeback mode
                _tsEndWbmode = Cycles;
                mctrl.wbmode_length.collect((int)(_tsEndWbmode - _tsStartWbmode));

                mctrl.rds_per_wb_mode.Collect(_rdsPerWbMode);
                mctrl.wbs_per_wb_mode.Collect(_wbsPerWbMode);
                _rdsPerWbMode = 0;
                _wbsPerWbMode = 0;
            }
            return Wbmode;
        }

        // Debug usage
        private void PrintReqState(Req req)
        {
            Console.WriteLine("Time={0} REQ=" + req.to_str() + " hit=" + is_row_hit(req), Cycles);
        }

        private void PrintCmdState(Cmd cmd)
        {
            Console.WriteLine("Time={0} CMD=" + cmd.to_str(), Cycles);
        }
    }
}