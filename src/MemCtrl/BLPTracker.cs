using System;
using System.Collections.Generic;
using Ramulator.MemReq;
using Ramulator.Sim;

namespace Ramulator.MemCtrl
{
    public class BLPTracker
    {
        private MemCtrl[] _mctrls;
        private int[] _blpPerproc;

        public BLPTracker(MemCtrl[] mctrls)
        {
            _mctrls = new MemCtrl[mctrls.Length];
            for (int i = 0; i < mctrls.Length; i++)
                _mctrls[i] = mctrls[i];
            _blpPerproc = new int[Config.N];
        }

        public void Tick()
        {
            if (!Config.mctrl.blp_tracking)
                return;

            Array.Clear(_blpPerproc, 0, _blpPerproc.Length);

            foreach (var t in _mctrls)
            {
                for (int r = 0; r < t.Rmax; r++)
                {
                    for (int b = 0; b < t.Bmax; b++)
                    {
                        List<Req> inflightq = t.Inflightqs[r, b];
                        if (inflightq.Count > 0)
                        {
                            Req req = inflightq[inflightq.Count - 1];
                            _blpPerproc[req.Pid]++;
                        }
                    }
                }
            }

            for (int pid = 0; pid < _blpPerproc.Length; pid++)
            {
                int myblp = _blpPerproc[pid];
                if (myblp == 0)
                    continue;

                Stat.procs[pid].service_blp.collect(myblp);
            }

            /* wblp */
            foreach (var mctrl in _mctrls)
            {
                if (!mctrl.Wbmode)
                    continue;

                int wbmodeBlp = 0;
                for (uint r = 0; r < mctrl.Rmax; r++)
                {
                    for (uint b = 0; b < mctrl.Bmax; b++)
                    {
                        List<Req> inflightq = mctrl.Inflightqs[r, b];
                        if (inflightq.Count > 0)
                        {
                            Req req = inflightq[inflightq.Count - 1];
                            _blpPerproc[req.Pid]++;
                            wbmodeBlp++;
                        }
                    }
                }
                Stat.mctrls[mctrl.cid].wbmode_blp.collect(wbmodeBlp);
            }
        }
    }
}