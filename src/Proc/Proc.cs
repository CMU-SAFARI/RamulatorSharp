using Ramulator.Mem;
using Ramulator.MemCtrl;
using System;
using System.Collections.Generic;
using Ramulator.MemReq;
using Ramulator.Sim;

namespace Ramulator.Proc
{
    public class Proc
    {
        public static readonly ulong NULL_ADDRESS = ulong.MaxValue;
        public static Random rand = new Random(0);

        //processor id
        private static int pmax = 0;

        public int pid;

        //components
        public InstWnd inst_wnd;

        public List<ulong> mshr;
        public List<Req> wb_q;

        // Store in-flight read request for the write cache misses
        public HashSet<ulong> read_write_q;

        //other components
        public Trace trace;

        //current status
        public ulong cycles;

        public int curr_cpu_inst_cnt;
        public Req curr_rd_req, curr_wb_req;

        //retry memory request
        private bool mctrl_retry = false;

        private bool mshr_retry = false;

        //etc: outstanding requests
        public int out_read_req;

        //etc: stats
        private ulong curr_megainst, prev_read_req, prev_write_req, prev_inst_cnt, consec_stalled;

        // Handles to access memory controllers
        public MemCtrl.MemCtrl[] mctrls;

        // Cache hierarchy
        private Cache l1c, l2c;

        //throttle
        public double throttle_fraction = 0;

        // For memory schedulers: using stats can give you false information b/c once a core finishes,
        // the stat object stops updating
        public ulong inst_count, mem_req_count, mem_rd_req_count;

        public Proc(string trace_fname, CacheHierarchy cache_hier)
        {
            pid = pmax;
            pmax++;

            // components
            inst_wnd = new InstWnd(Config.proc.inst_wnd_max);
            mshr = new List<ulong>(Config.proc.mshr_max);
            wb_q = new List<Req>(Config.proc.wb_q_max);
            read_write_q = new HashSet<ulong>();

            // traces
            Stat.procs[pid].trace_fname = trace_fname;
            trace = new Trace(pid, trace_fname);

            curr_rd_req = get_req();
            mctrls = new MemCtrl.MemCtrl[Config.mem.chan_max];

            // Assign caches
            if (Config.proc.cache_enabled)
            {
                l1c = cache_hier.L1List[pid];
                l2c = cache_hier.L2List[pid];
            }

            inst_count = 0;
            mem_req_count = 0;
            mem_rd_req_count = 0;
        }

        // Handles the timing for returning back cache hits from different levels of caches
        public void add_cache_hit_queue(Cache c, Req req)
        {
            LinkedList<Req> cache_hit_queue = c.get_hit_queue(req.Pid);
            req.TsDeparture = (long)(cycles + c.HitLatency);
            req.TsArrival = (long)cycles;
            cache_hit_queue.AddLast(req);
            // TODO: this should not be needed
            //inst_wnd.add(req.block_addr, true, false);
        }

        // Null upper_c means c is a L1 cache, otherwise L2
        public void service_cache_hit_queue(Cache c, Cache upper_c = null)
        {
            LinkedList<Req> hit_queue = c.get_hit_queue(pid);
            while (hit_queue.Count != 0)
            {
                Req req = hit_queue.First.Value;
                int hit_pid = req.Pid;
                Dbg.Assert(hit_pid == pid);
                if ((ulong)req.TsDeparture <= cycles)
                {
                    // Hit in L2 and move L2 $line to L1
                    if (upper_c != null)
                    {
                        Cache l1c = upper_c;
                        Dbg.AssertPrint(!l1c.in_cache(req.BlockAddr),
                                "$line from an L2 hit shouldn't be in L1.");
                        ulong l1c_wb_addr = l1c.cache_add(req.BlockAddr, req.Type, hit_pid);
                        // Dirty $line eviction from L1, check L2 first.
                        if (l1c_wb_addr != NULL_ADDRESS)
                        {
                            // Miss in L2
                            if (!c.is_cache_hit(l1c_wb_addr, ReqType.WRITE))
                            {
                                // Another potential wb from L2
                                ulong l2c_wb_addr = c.cache_add(l1c_wb_addr, ReqType.WRITE, hit_pid);
                                if (l2c_wb_addr != NULL_ADDRESS)
                                    gen_cache_wb_req(l2c_wb_addr);
                            }
                        }
                        Stat.procs[pid].l2_cache_hit_avg_latency.collect((int)(cycles - (ulong)req.TsArrival));
                    }
                    else
                        Stat.procs[pid].l1_cache_hit_avg_latency.collect((int)(cycles - (ulong)req.TsArrival));

                    // Simply hit in L1
                    hit_queue.RemoveFirst();
                    inst_wnd.set_ready(req.BlockAddr);
                    RequestPool.Enpool(req);
                }
                else
                    return;
            }
        }

        public void addWB(Req wb_req)
        {
            wb_q.Add(wb_req);
            Stat.procs[pid].write_misses.collect();
            Stat.procs[pid].wmpc.collect();
        }

        // Generate a new writeback request to memory from L2 dirty block eviction
        public void gen_cache_wb_req(ulong wb_addr)
        {
            Req wb_req = RequestPool.Depool();
            wb_req.Set(pid, ReqType.WRITE, wb_addr);
            bool wb_merge = wb_q.Exists(x => x.BlockAddr == wb_req.BlockAddr);
            if (!wb_merge)
                addWB(wb_req);
            else
                RequestPool.Enpool(wb_req);
        }

        // Callback function when a memory request is complete. This retires instructions or inserts data back into caches.
        public void recv_req(Req req)
        {
            // Install the rest of the words in the cacheline
            bool cw_contains_write = false;

            //stats
            if (!req.CpyGenReq)
            {
                Stat.procs[pid].read_req_served.collect();
                Stat.procs[pid].read_avg_latency.collect(req.Latency);
            }

            // Handles the read write request
            if (req.RdWr)
            {
                Dbg.Assert(read_write_q.Contains(req.BlockAddr));
                read_write_q.Remove(req.BlockAddr);
            }

            //free up instruction window and mshr
            bool contains_write = inst_wnd.set_ready(req.BlockAddr);
            contains_write |= cw_contains_write;
            mshr.RemoveAll(x => x == req.BlockAddr);

            Req wb_req = null;

            // Install cachelines and handle dirty block evictions
            if (Config.proc.cache_enabled)
                cache_handler(req, contains_write);
            else
            {
                Dbg.AssertPrint(!contains_write, "Inst window contains write reqeusts.");
                // Writeback based on the cache filtered traces
                wb_req = req.WbReq;
                if (wb_req != null)
                {
                    bool wb_merge = wb_q.Exists(x => x.BlockAddr == wb_req.BlockAddr);
                    if (!wb_merge)
                    {
                        addWB(wb_req);
                    }
                    else
                    {
                        RequestPool.Enpool(wb_req);
                    }
                }
            }

            //destory req
            RequestPool.Enpool(req);
            out_read_req--;
        }

        private void cache_handler(Req req, bool contains_write)
        {
            // Make sure req.wb_req is null
            Dbg.Assert(req.WbReq == null);

            // Shouldn't be in the caches
            Dbg.Assert(!l2c.in_cache(req.BlockAddr));
            Dbg.Assert(!l1c.in_cache(req.BlockAddr));

            // Make the request type back to write again on read-write, so we can mark dirty
            if (req.RdWr || contains_write || req.DirtyInsert)
                req.Type = ReqType.WRITE;

            // NON-INCLUSIVE PROPERTY based on Sim et al., ISCA 2012
            // Install cache lines in both L1 and L2
            ulong l2c_wb_addr = l2c.cache_add(req.BlockAddr, req.Type, pid);
            // Dirty $line eviction from L2, simply write it back. Another dirty
            // copy may still be in L1. Leave it alone.
            if (l2c_wb_addr != NULL_ADDRESS)
                gen_cache_wb_req(l2c_wb_addr);

            ulong l1c_wb_addr = l1c.cache_add(req.BlockAddr, req.Type, pid);
            // Dirty $line eviction from L1, check L2 first.
            if (l1c_wb_addr != NULL_ADDRESS)
            {
                // Miss in L2
                if (!l2c.is_cache_hit(l1c_wb_addr, ReqType.WRITE))
                {
                    // Another potential wb from L2
                    l2c_wb_addr = l2c.cache_add(l1c_wb_addr, ReqType.WRITE, pid);
                    if (l2c_wb_addr != NULL_ADDRESS)
                        gen_cache_wb_req(l2c_wb_addr);
                }
            }
        }

        public void recv_copy_req(Req req)
        {
            //stats
            Stat.procs[pid].copy_req_served.collect();
            Stat.procs[pid].copy_avg_latency.collect(req.Latency);

            //free up instruction window and mshr
            bool contains_write = inst_wnd.set_ready(req.BlockAddr, true);
            mshr.RemoveAll(x => x == req.BlockAddr);
            Dbg.AssertPrint(!contains_write, "Inst window contains write reqeusts. COPY is not supported in cache mode.");
            Dbg.Assert(req.WbReq == null);

            //destory req
            RequestPool.Enpool(req);
        }

        public void recv_wb_req(Req req)
        {
            //stats
            Stat.procs[pid].write_req_served.collect();
            Stat.procs[pid].write_avg_latency.collect(req.Latency);

            //destroy req
            RequestPool.Enpool(req);
        }

        public Req get_req()
        {
            Dbg.Assert(curr_cpu_inst_cnt == 0);

            Req wb_req = null;
            trace.get_req(ref curr_cpu_inst_cnt, out curr_rd_req, out wb_req);
            if (curr_rd_req == null)
                return null;

            curr_rd_req.WbReq = wb_req;
            return curr_rd_req;
        }

        public bool issue_wb_req(Req wb_req)
        {
            bool mctrl_ok = insert_mctrl(wb_req);
            return mctrl_ok;
        }

        public bool reissue_rd_req()
        {
            //retry mshr
            if (mshr_retry)
            {
                Dbg.Assert(!mctrl_retry);

                //retry mshr
                Dbg.Assert(curr_rd_req.Type == ReqType.READ || curr_rd_req.Type == ReqType.COPY);
                bool mshr_ok = insert_mshr(curr_rd_req);
                if (!mshr_ok)
                    return false;

                //success
                mshr_retry = false;

                //check if true miss
                bool false_miss = inst_wnd.is_duplicate(curr_rd_req.BlockAddr);
                Dbg.Assert(!false_miss);

                //retry mctrl
                mctrl_retry = true;
            }

            //retry mctrl
            if (mctrl_retry)
            {
                Dbg.Assert(!mshr_retry);

                //retry mctrl
                Dbg.Assert(curr_rd_req.Type == ReqType.READ || curr_rd_req.Type == ReqType.COPY);
                bool mctrl_ok = insert_mctrl(curr_rd_req);
                if (!mctrl_ok)
                    return false;

                //success
                mctrl_retry = false;
                return true;
            }

            //should never get here
            throw new System.Exception("Processor: Reissue Request");
        }

        public MemAddr inst_wnd_head_addr()
        {
            ulong block_addr = inst_wnd.head();
            ulong paddr = block_addr << Config.proc.block_size_bits;
            MemAddr addr = MemMap.Translate(paddr);
            return addr;
        }

        // Send a read request for write
        private void convert_to_read_write(ref Req rd_wr_req)
        {
            // Change to read
            rd_wr_req.Type = ReqType.READ;
            rd_wr_req.RdWr = true;
            ulong baddr = rd_wr_req.BlockAddr;
            // Make sure it's not a dup....
            Dbg.Assert(!read_write_q.Contains(baddr));
            Dbg.Assert(read_write_q.Count <= Config.proc.read_write_q_max);
            read_write_q.Add(baddr);
            // Remove from the instruction window -- non-blocking write
            inst_wnd.set_ready(baddr);
        }

        public void issue_insts(bool issued_rd_req)
        {
            //issue instructions
            for (int i = 0; i < Config.proc.ipc; i++)
            {
                Dbg.Assert(curr_rd_req != null);
                if (curr_rd_req == null)
                    return;

                // Stats
                if (inst_wnd.is_full())
                {
                    if (i == 0)
                    {
                        Stat.procs[pid].stall_inst_wnd.collect();
                        consec_stalled++;
                    }
                    return;
                }

                //cpu instructions
                if (curr_cpu_inst_cnt > 0)
                {
                    curr_cpu_inst_cnt--;
                    inst_wnd.add(0, false, true, 0); // word oblivious
                    continue;
                }

                //only one memory instruction can be issued per cycle
                if (issued_rd_req)
                    return;

                // Ideal memory
                if (Config.proc.ideal_memory)
                {
                    Dbg.AssertPrint(!Config.proc.cache_enabled, "Cache is not supported in ideal memory mode.");
                    if (curr_rd_req.WbReq != null)
                        RequestPool.Enpool(curr_rd_req.WbReq);
                    RequestPool.Enpool(curr_rd_req);
                    curr_rd_req = get_req();
                    return;
                }

                // Need to mark if an instruction is a write on cache mode or COPY for a copy instruction
                inst_wnd.add(curr_rd_req.BlockAddr, true, false, curr_rd_req.WordOffset,
                    (curr_rd_req.Type == ReqType.WRITE) && Config.proc.cache_enabled, curr_rd_req.Type == ReqType.COPY);

                // check if true miss --
                bool false_miss = inst_wnd.is_duplicate(curr_rd_req.BlockAddr);
                // COPY is a special instruction, so we don't care about if its address is a duplicate of other instructions
                if (false_miss && Config.proc.issue_on_dup_req && curr_rd_req.Type != ReqType.COPY)
                {
                    Dbg.Assert(curr_rd_req.WbReq == null);
                    RequestPool.Enpool(curr_rd_req);
                    curr_rd_req = get_req();
                    continue;
                }

                // STATS
                collect_inst_stats();

                // Caches
                if (Config.proc.cache_enabled && curr_rd_req.Type != ReqType.COPY)
                {
                    // Check for in-flight rd_wr_q.
                    // Since write is duplicate, drop it....
                    bool in_rd_wr_q = read_write_q.Contains(curr_rd_req.BlockAddr);
                    // L1
                    if (l1c.is_cache_hit(curr_rd_req.BlockAddr, curr_rd_req.Type))
                    {
                        Dbg.AssertPrint(!in_rd_wr_q, "Both in rd_wr_q and L1 cache baddr=" + curr_rd_req.BlockAddr);
                        // HIT: Add to l1 cache hit queue to model the latency
                        add_cache_hit_queue(l1c, curr_rd_req);
                        curr_rd_req = get_req();
                        issued_rd_req = true;
                        continue;
                    }
                    // L2
                    if (l2c.is_cache_hit(curr_rd_req.BlockAddr, curr_rd_req.Type))
                    {
                        Dbg.Assert(!in_rd_wr_q);
                        // HIT: Add to l2 cache hit queue to model the latency,
                        // add to l1 cache after it is served from the hit queue
                        add_cache_hit_queue(l2c, curr_rd_req);
                        curr_rd_req = get_req();
                        issued_rd_req = true;
                        continue;
                    }
                    if (in_rd_wr_q)
                    {
                        if (curr_rd_req.Type == ReqType.WRITE)
                        {
                            inst_wnd.set_ready(curr_rd_req.BlockAddr);
                        }
                        RequestPool.Enpool(curr_rd_req);
                        curr_rd_req = get_req();
                        issued_rd_req = true;
                        continue;
                    }
                    // If write allocate -- 1. need to make sure the following read request
                    // detects this reading request generated from write
                    // 2. don't stall the instruction window
                    // Make it into a read request, then on receving the
                    // request, put them into the cache and mark them dirty.
                    if (curr_rd_req.Type == ReqType.WRITE)
                        convert_to_read_write(ref curr_rd_req);
                }

                // **** GO TO MEMORY ****
                //try mshr
                bool mshr_ok = insert_mshr(curr_rd_req);
                if (!mshr_ok)
                {
                    mshr_retry = true;
                    return;
                }

                //try memory controller
                bool mctrl_ok = insert_mctrl(curr_rd_req);
                if (!mctrl_ok)
                {
                    mctrl_retry = true;
                    return;
                }

                //issued memory request
                issued_rd_req = true;

                //get new read request
                curr_rd_req = get_req();
            }
        }

        private void collect_inst_stats()
        {
            if (curr_rd_req.Type == ReqType.READ)
            {
                Stat.procs[pid].read_misses.collect();
                Stat.procs[pid].rmpc.collect();
            }
            else if (curr_rd_req.Type == ReqType.WRITE)
            {
                Stat.procs[pid].write_misses.collect();
                Stat.procs[pid].wmpc.collect();
            }
            else if (curr_rd_req.Type == ReqType.COPY)
            {
                Stat.procs[pid].copy_misses.collect();
                Stat.procs[pid].cmpc.collect();
            }
        }

        public void tick()
        {
            /*** Preamble ***/
            cycles++;
            Stat.procs[pid].cycle.collect();

#if DEBUG
            if (cycles % 1000000 == 0)
                Console.WriteLine("Cycles {0} IPC {1}", cycles, (double)(Stat.procs[pid].ipc.Count) / cycles);
#endif

            //starved for way too long: something's wrong
            if (consec_stalled > 1000000)
            {
                string str = "Cycles=" + cycles + " -- Inst Window stalled for too long: window head=" + inst_wnd.head();
                Dbg.AssertPrint(false, str);
            }

            // STATS
            ulong inst_cnt = Stat.procs[pid].ipc.Count;
            retired_inst_stats(inst_cnt);

            // Check cache hits
            if (Config.proc.cache_enabled)
            {
                service_cache_hit_queue(l1c);
                service_cache_hit_queue(l2c, l1c);
            }

            /*** Throttle ***/
            if (throttle_fraction > 0)
            {
                if (rand.NextDouble() < throttle_fraction)
                    return;
            }

            /*** Retire ***/
            int retired = inst_wnd.retire(Config.proc.ipc);
            // Deduct those instructions due to expanded copy requests
            if (Config.mctrl.copy_method == COPY.MEMCPY)
            {
                if (trace.copy_to_req_ipc_deduction > 0 && retired > 0)
                {
                    if (trace.copy_to_req_ipc_deduction > (ulong)retired)
                    {
                        trace.copy_to_req_ipc_deduction -= (ulong)retired;
                        retired = 0;
                    }
                    else
                    {
                        retired -= (int)trace.copy_to_req_ipc_deduction;
                        trace.copy_to_req_ipc_deduction = 0;
                    }
                }
            }
            Stat.procs[pid].ipc.collect(retired);
            Stat.caches[0].total_system_inst_executed.collect(retired);
            inst_count++;
            if (retired > 0)
                consec_stalled = 0;
            else
                consec_stalled++;

            /*** Issue writeback request ***/
            if (Config.proc.wb && wb_q.Count > 0)
            {
                bool wb_ok = issue_wb_req(wb_q[0]);
                if (wb_ok)
                {
                    wb_q.RemoveAt(0);
                }

                //writeback stall
                bool stalled_wb = wb_q.Count > Config.proc.wb_q_max;
                if (stalled_wb)
                    return;
            }

            /*** Reissue previous read request ***/
            bool issued_rd_req = false;
            if (mshr_retry || mctrl_retry)
            {
                Dbg.Assert(curr_rd_req != null && curr_cpu_inst_cnt == 0);

                //mshr/mctrl stall
                bool reissue_ok = reissue_rd_req();
                if (!reissue_ok)
                    return;

                //reissue success
                Dbg.Assert(!mshr_retry && !mctrl_retry);
                issued_rd_req = true;
                curr_rd_req = get_req();
            }

            /*** Issue instructions ***/
            Dbg.Assert(curr_rd_req != null);
            issue_insts(issued_rd_req);
        }

        private void retired_inst_stats(ulong inst_cnt)
        {
            if (cycles != 0 && cycles % 10000000 == 0)
            {
                Stat.procs[pid].insts_per_quantum.EndQuantum(inst_cnt - prev_inst_cnt);
                prev_inst_cnt = inst_cnt;
            }
            if (inst_cnt != 0 && inst_cnt % 1000000 == 0)
            {
                ulong megainst = inst_cnt / 1000000;
                if (megainst > curr_megainst)
                {
                    curr_megainst = megainst;

                    ulong read_req = Stat.procs[pid].read_req.Count;
                    Stat.procs[pid].reads_per_megainst.EndQuantum(read_req - prev_read_req);
                    prev_read_req = read_req;

                    ulong write_req = Stat.procs[pid].write_req.Count;
                    Stat.procs[pid].writes_per_megainst.EndQuantum(write_req - prev_write_req);
                    prev_write_req = write_req;
                }
            }
        }

        private bool insert_mshr(Req req)
        {
            if (mshr.Count == mshr.Capacity)
            {
                Stat.procs[pid].stall_mshr.collect();
                return false;
            }
            mshr.Add(req.BlockAddr);
            return true;
        }

        private bool insert_mctrl(Req req)
        {
            MemAddr addr = req.Addr;

            //failure
            if (Sim.Sim.Mctrls[addr.cid].is_q_full(pid, req.Type, addr.rid, addr.bid))
            {
                if (req.Type == ReqType.READ)
                    Stat.procs[req.Pid].stall_read_mctrl.collect();
                else if (req.Type == ReqType.WRITE)
                    Stat.procs[req.Pid].stall_write_mctrl.collect();
                else if (req.Type == ReqType.COPY)
                    Stat.procs[req.Pid].stall_copy_mctrl.collect();
                return false;
            }

            //success
            send_req(req);
            return true;
        }

        private void send_req(Req req)
        {
            switch (req.Type)
            {
                case ReqType.READ:
                    Stat.procs[pid].read_req.collect();
                    Stat.caches[0].rd_req_word_offset.Collect((ulong)req.WordOffset);
                    req.Callback = new Callback(recv_req);
                    out_read_req++;
                    mem_rd_req_count++;
                    break;

                case ReqType.WRITE:
                    Stat.procs[pid].write_req.collect();
                    req.Callback = new Callback(recv_wb_req);
                    break;

                case ReqType.COPY:
                    Stat.procs[pid].copy_req.collect();
                    req.Callback = new Callback(recv_copy_req);
                    break;
            }

            mem_req_count++;
            Stat.procs[pid].req.collect();
            Sim.Sim.Mctrls[req.Addr.cid].enqueue_req(req);
        }

        public override string ToString()
        {
            return "Processor " + pid;
        }
    }
}
