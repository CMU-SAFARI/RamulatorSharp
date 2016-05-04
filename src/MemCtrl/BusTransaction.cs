using Ramulator.Mem;

namespace Ramulator.MemCtrl
{
    public struct BusTransaction
    {
        public MemAddr Addr;
        public long ts;

        public BusTransaction(MemAddr addr, long ts)
        {
            Addr = addr;
            this.ts = ts;
        }
    }
}