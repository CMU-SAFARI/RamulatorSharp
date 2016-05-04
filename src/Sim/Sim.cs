using Ramulator.Mem;
using Ramulator.MemCtrl;
using Ramulator.MemReq;
using Ramulator.MemReq.Auxiliary;
using Ramulator.Proc;
using System;
using System.Diagnostics;

namespace Ramulator.Sim
{
    internal static class Dbg
    {
        // *IMPORTANT* mono's default Assert() statement just keeps on trucking even if it fails
        public static void Assert(bool val)
        {
            Debug.Assert(val);
            if (!val)
            {
                throw new Exception("ASSERT EXCEPTION!!!");
            }
        }

        public static void AssertPrint(bool val, string statement)
        {
            Debug.Assert(val);
            if (!val)
                throw new Exception(statement);
        }

        public static void Print(string statement)
        {
            Console.WriteLine(Sim.cycles.ToString() + ": " + statement);
        }

        [Conditional("DEBUG")]
        public static void DebugBreak()
        {
            if (Debugger.IsAttached)
                Debugger.Break();
        }
    }

    public class Sim
    {
        public static Proc.Proc[] Procs;
        public static Xbar Xbar;
        public static MemCtrl.MemCtrl[] Mctrls;
        public static MemWBMode.MemWBMode Mwbmode;
        public static BLPTracker Blptracker;
        public static CacheHierarchy Caches;
        public Stat Stat;

        // Maximum number of processors supported
        public static int PROC_MAX_LIMIT = 128;

        // Number of clock cycles past
        public static ulong cycles = 0;

        public static Random rand = new Random(0);

        public Sim()
        {
        }

        public void Initialize()
        {
            Stat = new Stat();

            // Crossbar
            Xbar = new Xbar();

            // ddr3
            DDR3DRAM ddr3 = new DDR3DRAM(Config.mem.ddr3_type,
                    Config.mem.clock_factor, Config.mem.tRTRS, Config.mem.tWR,
                    Config.mem.tWTR, Config.mem.tBL, Config.mem.bank_max,
                    Config.mem.subarray_max, Config.mem.col_max,
                    Config.mem.tRA, Config.mem.tWA, Config.mem.tREFI,
                    Config.mem.tRFC, Config.mem.tRP, Config.mem.tRCD);
            uint cmax = Config.mem.chan_max;
            uint rmax = Config.mem.rank_max;

            // randomized page table
            const ulong page_size = 4 * 1024;
            PageRandomizer prand = new PageRandomizer(page_size, ddr3.ROW_MAX);
            Req.Prand = prand;

            // sequential page table
            PageSequencer pseq = new PageSequencer(page_size, cmax, rmax, ddr3.BANK_MAX);
            Req.Pseq = pseq;

            // Contiguous physical page allocation
            ContiguousAllocator pcontig = new ContiguousAllocator(page_size, cmax * rmax * ddr3.ROW_MAX * ddr3.DEVICE_WIDTH);
            Req.Pcontig = pcontig;

            // memory mapping
            MemMap.Init(Config.mem.map_type, Config.mem.chan_max, Config.mem.rank_max, Config.mem.col_per_subrow, ddr3);

            // Cache hierarchy
            if (Config.proc.cache_enabled)
            {
                Caches = new CacheHierarchy(Config.N);
            }

            // processors
            Procs = new Proc.Proc[Config.N];
            for (int p = 0; p < Config.N; p++)
                Procs[p] = new Proc.Proc(Config.traceFileNames[p], Caches);

            // memory controllers
            Mctrls = new MemCtrl.MemCtrl[cmax];
            for (int i = 0; i < Mctrls.Length; i++)
            {
                Mctrls[i] = new MemCtrl.MemCtrl(rmax, ddr3);
                // Add ref handles to processors
                for (int p = 0; p < Config.N; p++)
                {
                    Procs[p].mctrls[i] = Mctrls[i];
                    Mctrls[i].ProcHandles[p] = Procs[p];
                }
            }

            // memory schedulers
            MemSched.MemSched[] rscheds = new MemSched.MemSched[cmax];
            for (int i = 0; i < cmax; i++)
            {
                Object[] args = { Mctrls[i], Mctrls };
                rscheds[i] = Activator.CreateInstance(Config.sched.typeof_sched_algo, args) as MemSched.MemSched;
            }

            MemSched.MemSched[] wscheds = new MemSched.MemSched[cmax];
            for (int i = 0; i < cmax; i++)
            {
                Object[] args = { Mctrls[i], Mctrls };
                wscheds[i] = Activator.CreateInstance(Config.sched.typeof_wbsched_algo, args) as MemSched.MemSched;
            }

            for (int i = 0; i < cmax; i++)
            {
                Mctrls[i].Rsched = rscheds[i];
                Mctrls[i].Wsched = wscheds[i];

                rscheds[i].Initialize();
                wscheds[i].Initialize();
            }

            // WB mode
            Mwbmode = Activator.CreateInstance(Config.mctrl.typeof_wbmode_algo, new Object[] { Mctrls }) as MemWBMode.MemWBMode;
            for (int i = 0; i < cmax; i++)
            {
                Mctrls[i].Mwbmode = Mwbmode;
            }

            // BLP tracker
            Blptracker = new BLPTracker(Mctrls);
        }

        public void RunAll()
        {
            bool[] isDone = new bool[Config.N];
            for (int i = 0; i < Config.N; i++)
            {
                isDone[i] = false;
            }

            bool isWarmup = Config.proc.cache_enabled && (Config.warmup_cycle_max > 0);
            bool finished = false;
            while (!finished)
            {
                finished = true;

                // Processors
                int pid = rand.Next(Config.N);
                for (int i = 0; i < Config.N; i++)
                {
                    Proc.Proc currProc = Procs[pid];
                    if (isDone[pid] == false)
                        currProc.tick();
                    pid = (pid + 1) % Config.N;
                }

                // Memory controllers
                for (int i = 0; i < Config.mem.chan_max; i++)
                {
                    Mctrls[i].Tick();
                }

                // BLP tracker
                Blptracker.Tick();

                // XBAR
                Xbar.Tick();

                // Progress simulation time
                cycles++;

                // Warming up the cache
                if (cycles >= Config.warmup_cycle_max && isWarmup)
                {
                    isWarmup = false;
                    reset_stats();
                }
                if (isWarmup)
                {
                    finished = false;
                    continue;
                }

                // Case #1: instruction constrained simulation
                switch (Config.sim_type)
                {
                    case Config.SIM_TYPE.INST:
                        for (int p = 0; p < Config.N; p++)
                        {
                            if (isDone[p]) continue;

                            if (Stat.procs[p].ipc.Count >= Config.sim_inst_max)
                            {
                                // Simulation is now finished for this processor
                                finish_proc(p);
                                isDone[p] = true;
                            }
                            else
                            {
                                // Simulation is still unfinished for this processor
                                finished = false;
                            }
                        }
                        break;
                    case Config.SIM_TYPE.CYCLE:
                        if (cycles >= Config.sim_cycle_max)
                        {
                            finish_proc();
                            finished = true;
                        }
                        else
                        {
                            finished = false;
                        }
                        break;
                    case Config.SIM_TYPE.COMPLETION:
                        for (int p = 0; p < Config.N; p++)
                        {
                            if (isDone[p]) continue;

                            if (Procs[p].trace.finished)
                            {
                                // Simulation is now finished for this processor
                                finish_proc(p);
                                isDone[p] = true;
                            }
                            else
                            {
                                // Simulation is still unfinished for this processor
                                finished = false;
                            }
                        }
                        break;
                }
            }
        }

        public void reset_stats()
        {
            foreach (MemCtrlStat mctrl in Stat.mctrls)
                mctrl.Reset();
            foreach (BusStat bus in Stat.busses)
                bus.Reset();
            foreach (BankStat bank in Stat.banks)
                bank.Reset();
            foreach (CacheStat c in Stat.caches)
                c.Reset();
            foreach (ProcStat p in Stat.procs)
                p.Reset();
            cycles = 0;
            Console.WriteLine("Warm-up completes. Reset Stats!!!!");
        }

        public void Finish()
        {
            foreach (MemCtrlStat mctrl in Stat.mctrls)
            {
                mctrl.Finish(Sim.cycles);
            }
            foreach (BusStat bus in Stat.busses)
            {
                bus.Finish(Sim.cycles);
            }
            foreach (BankStat bank in Stat.banks)
            {
                bank.Finish(Sim.cycles);
            }
            foreach (CacheStat c in Stat.caches)
                c.Finish(Sim.cycles);
        }

        private static void finish_proc()
        {
            for (int pid = 0; pid < Config.N; pid++)
            {
                finish_proc(pid);
            }
        }

        private static void finish_proc(int pid)
        {
            Stat.procs[pid].Finish(Sim.cycles);
            foreach (MemCtrlStat mctrl in Stat.mctrls)
            {
                mctrl.Finish(Sim.cycles, pid);
            }
            foreach (BankStat bank in Stat.banks)
            {
                bank.Finish(Sim.cycles, pid);
            }
        }
    }
}