using Ramulator.Mem;
using Ramulator.MemCtrl;
using Ramulator.MemReq.Auxiliary;
using Ramulator.Sim;
using System;
using System.Collections.Generic;

namespace Ramulator.MemReq
{
    public enum ReqType
    {
        READ,
        WRITE,
        REFRESH,
        REFRESH_BANK,
        COPY
    };

    public enum CopyRange
    {
        CHAN,
        RANK,
        BANK,
        INTER_SA,
        INTRA_SA
    }

    public class Req
    {
        /* Page mapping methods: Random, sequentil, and contiguous */
        public static PageRandomizer Prand;
        public static PageSequencer Pseq;
        public static ContiguousAllocator Pcontig;

        // state
        public int Pid;

        public ReqType Type;

        // Used to indicate if a particular read or write request is generated due to a copy request
        public bool CpyGenReq;

        // address
        public ulong TracePaddr;

        public ulong Paddr, CopyDestPaddr;
        public ulong BlockAddr, CopyDestBlockAddr;
        public MemAddr Addr, CopyDestMemAddr;

        // timestamp
        public long TsArrival;

        public long TsDeparture;
        public int Latency;
        public bool CacheHit;
        public bool DirtyInsert;

        // associated write-back request
        public Req WbReq;

        // callback
        public Callback Callback;

        // was an activate issued on this request's behalf?
        public bool RequiredActivate;

        //scheduling-related
        public bool Marked;

        // Read-write from the caches
        public bool RdWr;

        // Which word to load from a cacheline
        public int WordOffset;

        // ctor
        public Req() { }

        public void Set(int pid, ReqType type, ulong paddr)
        {
            // state
            Pid = pid;
            Type = type;

            // address
            TracePaddr = paddr;

            if (Config.mctrl.page_randomize)
                Paddr = Prand.get_paddr(paddr);
            else if (Config.mctrl.page_sequence)
                Paddr = Pseq.get_paddr(paddr);
            else if (Config.mctrl.page_contiguous)
                Paddr = Pcontig.get_paddr(paddr);
            else
                Paddr = paddr;
            BlockAddr = Paddr >> Config.proc.block_size_bits;
            Addr = MemMap.Translate(Paddr);

            Stat.procs[pid].allocated_physical_pages.collect();

            reset_timing();

            // Word offset
            ulong pwo = (Paddr & (63)) >> 2;
            Dbg.AssertPrint(pwo == ((paddr & (63)) >> 2),
                    "Word offset should be the same for both virtual and physical addresses.");
            Dbg.AssertPrint(pwo < 16, "There should be only 8 words in a cacheline=" + pwo);
            WordOffset = (int)pwo;
        }

        public void reset_timing()
        {
            TsArrival = -1;
            TsDeparture = -1;
            Latency = -1;
            CacheHit = false;
            WbReq = null;
            Callback = null;
            RequiredActivate = false;
            Marked = false;
            RdWr = false;
            DirtyInsert = false;
            CpyGenReq = false;
        }

        public void Reset()
        {
            Pid = -1;
            Type = ReqType.READ;
            CacheHit = false;
            TracePaddr = 0;
            Paddr = 0;
            BlockAddr = 0;
            Addr.Reset();
            TsArrival = -1;
            TsDeparture = -1;
            Latency = -1;
            WbReq = null;
            Callback = null;
            RequiredActivate = false;
            Marked = false;
            RdWr = false;
            DirtyInsert = false;
            CpyGenReq = false;
        }

        public string to_str()
        {
            return String.Format("{0,5} C{1} R{2} B{3} S{4} ROW{5} COL{6} rd_wr{7} block_addr{8}", Type.ToString(), Addr.cid, Addr.rid, Addr.bid, Addr.said, Addr.rowid, Addr.colid, RdWr, BlockAddr);
        }
    }

    public class RequestPool
    {
        private const int RECYCLE_MAX = 10000;
        private static LinkedList<Req> _reqPool = new LinkedList<Req>();

        static RequestPool()
        {
            for (int i = 0; i < RECYCLE_MAX; i++)
                _reqPool.AddFirst(new Req());
        }

        public static void Enpool(Req req)
        {
            req.Reset();
            _reqPool.AddLast(req);
        }

        public static Req Depool()
        {
            Dbg.Assert(_reqPool.First != null);
            Req req = _reqPool.First.Value;
            _reqPool.RemoveFirst();
            return req;
        }

        public bool IsEmpty()
        {
            return _reqPool.Count == 0;
        }

        public static int count()
        {
            return _reqPool.Count;
        }
    }
}