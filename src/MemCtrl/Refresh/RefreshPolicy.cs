using Ramulator.Mem;
using Ramulator.MemReq;
using Ramulator.Sim;

namespace Ramulator.MemCtrl.Refresh
{
    public enum RefreshEnum
    {
        NONE,
        AUTO,           // REFab
        AUTO_BANK,      // REFpb
    }

    public class RefreshPolicy
    {
        public Ramulator.MemCtrl.MemCtrl Mctrl;
        public ulong Trefi;
        public long Cycles;

        public RefreshPolicy()
        {
        }

        public RefreshPolicy(MemCtrl mctrl, ulong trefi)
        {
            Mctrl = mctrl;
            Trefi = trefi / Config.mctrl.refresh_frequency;
            Cycles = 0;
        }

        public virtual void Tick()
        {
        }

        public virtual void InjectRefresh()
        {
        }

        public virtual void InjectEarlyRefresh(int rankIdx, int bankIdx)
        {
        }

        public virtual bool TryInjectRefresh(int rankIdx, int bankIdx)
        {
            return false;
        }

        public virtual bool ForceInjectRefresh(int rankIdx, int bankIdx)
        {
            return false;
        }

        // Generate a temporary refresh request that is used for probing the availability of DRAM to issue a refresh
        // Giving a bank idx of -1 makes an all-bank refresh request
        public virtual Req GenRefreshBankReq(int rankIdx, int bankIdx)
        {
            Req req = new Req
            {
                Type = (bankIdx == -1) ? ReqType.REFRESH : ReqType.REFRESH_BANK,
                TsArrival = Cycles
            };
            // Set up the refresh target address
            MemAddr addr = new MemAddr();
            addr.Reset();
            addr.cid = Mctrl.cid;
            addr.rid = (uint)rankIdx;
            if (bankIdx != -1)
                addr.bid = (uint)bankIdx;
            req.Addr = addr;
            return req;
        }
    }
}