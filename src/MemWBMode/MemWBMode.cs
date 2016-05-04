using Ramulator.MemCtrl;

namespace Ramulator.MemWBMode
{
    public abstract class MemWBMode
    {
        protected int Cmax;
        public MemCtrl.MemCtrl[] Mctrls;
        public bool[] WbMode;
        public ulong Cycles;

        public MemWBMode(MemCtrl.MemCtrl[] mctrls)
        {
            Cmax = mctrls.Length;
            Mctrls = mctrls;
            WbMode = new bool[Cmax];
        }

        public abstract void Tick(uint cid);

        public bool is_wb_mode(uint cid)
        {
            return WbMode[cid];
        }

        public virtual void issued_write_cmd(Cmd cmd)
        {
        }

        protected bool is_writeq_empty(uint cid)
        {
            MemCtrl.MemCtrl mctrl = Mctrls[cid];
            return mctrl.Wload == 0;
        }

        protected bool is_writeq_full(uint cid)
        {
            MemCtrl.MemCtrl mctrl = Mctrls[cid];
            return mctrl.Wload == mctrl.WriteqMax;
        }

        protected bool is_readq_empty(uint cid)
        {
            MemCtrl.MemCtrl mctrl = Mctrls[cid];
            return mctrl.Rload == 0;
        }
    }
}