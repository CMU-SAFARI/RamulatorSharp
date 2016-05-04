using Ramulator.MemCtrl;
using Ramulator.Sim;

namespace Ramulator.MemWBMode
{
    public class DecoupledWBFullServeN : MemWBMode
    {
        public uint ServeMax;
        public uint[] ServeCnt;

        public DecoupledWBFullServeN(MemCtrl.MemCtrl[] mctrls)
            : base(mctrls)
        {
            ServeMax = Config.mctrl.serve_max;
            ServeCnt = new uint[Cmax];
        }

        public override void issued_write_cmd(Cmd cmd)
        {
            Dbg.Assert(cmd.is_write());
            uint cid = cmd.Addr.cid;
            ServeCnt[cid]++;
        }

        public override void Tick(uint cid)
        {
            if (cid != 0)
                return;

            Cycles++;

            // check for end of writeback mode
            for (uint i = 0; i < Cmax; i++)
            {
                if (!WbMode[i])
                    continue;

                if (ServeCnt[i] < ServeMax)
                    continue;

                WbMode[i] = false;
            }

            // check for start of writeback mode
            for (uint i = 0; i < Cmax; i++)
            {
                if (WbMode[i])
                    continue;

                if (!is_writeq_full(i))
                    continue;

                ServeCnt[i] = 0;
                WbMode[i] = true;
            }
        }
    }
}