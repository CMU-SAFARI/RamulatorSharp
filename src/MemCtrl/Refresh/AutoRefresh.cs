using Ramulator.Mem;
using System;
using System.Collections.Generic;
using Ramulator.MemReq;
using Ramulator.Sim;

namespace Ramulator.MemCtrl.Refresh
{
    public class AutoRefresh : RefreshPolicy
    {
        public ulong Time;

        // Assume subarrays (and banks) are refreshed sequentially in each rank
        public uint[] SaCounters { get; protected set; }

        public uint[] BankCounters { get; protected set; }
        public uint[,] SaCountersPerbank;
        protected uint SaCounterMax, BankCounterMax;

        public AutoRefresh(MemCtrl mctrl, ulong trefi)
            : base(mctrl, trefi)
        {
            Time = 0;
            SaCounters = new uint[Config.mem.rank_max];
            BankCounters = new uint[Config.mem.rank_max];
            SaCountersPerbank = new uint[mctrl.Rmax, mctrl.Bmax];
            Array.Clear(SaCounters, 0, (int)Config.mem.rank_max);
            Array.Clear(BankCounters, 0, (int)Config.mem.rank_max);
            Array.Clear(SaCountersPerbank, 0, (int)(mctrl.Rmax * mctrl.Bmax));
            SaCounterMax = Config.mem.subarray_max;
            BankCounterMax = Config.mem.bank_max;
            Cycles = 0;
        }

        public override void Tick()
        {
            Time += 1;
            Cycles++;
            if (Time < Trefi)
                return;
            InjectRefresh();
            Time = 0;
        }

        public override void InjectRefresh()
        {
            // Rank-level refresh: refresh all banks at once
            if (!Config.mctrl.b_refresh_bank)
            {
                for (uint r = 0; r < Mctrl.Rmax; r++)
                    RefreshRank(r);
            }
            else // Bank-level refresh
            {
                for (uint r = 0; r < Mctrl.Rmax; r++)
                    RefreshBank(r, BankCounters[r]);
            }
        }

        protected void RefreshRank(uint rankIdx)
        {
            List<Req> q = Mctrl.Refrankqs[rankIdx];
            Req req = RequestPool.Depool();
            req.Type = ReqType.REFRESH;
            req.TsArrival = Cycles;

            // Set up the refresh target address
            MemAddr addr = new MemAddr();
            addr.Reset();
            addr.cid = Mctrl.cid;
            addr.rid = rankIdx;
            addr.said = SaCounters[rankIdx];
            req.Addr = addr;
            q.Add(req);

            SaCounters[rankIdx]++;
            if (SaCounters[rankIdx] == SaCounterMax)
                SaCounters[rankIdx] = 0;
        }

        protected void RefreshBank(uint rankIdx, uint bankIdx)
        {
            List<Req> q = Mctrl.Refbankqs[rankIdx, bankIdx];
            Req req = RequestPool.Depool();
            req.Type = ReqType.REFRESH_BANK;
            req.TsArrival = Cycles;

            // Set up the refresh target address
            MemAddr addr = new MemAddr();
            addr.Reset();
            addr.cid = Mctrl.cid;
            addr.rid = rankIdx;
            addr.bid = bankIdx;
            addr.said = SaCountersPerbank[rankIdx, bankIdx];
            req.Addr = addr;
            q.Add(req);

            // Update states
            BankCounters[rankIdx]++;
            if (BankCounters[rankIdx] == BankCounterMax)
                BankCounters[rankIdx] = 0;

            SaCountersPerbank[rankIdx, bankIdx]++;
            if (SaCountersPerbank[rankIdx, bankIdx] == SaCounterMax)
                SaCountersPerbank[rankIdx, bankIdx] = 0;
        }
    }
}