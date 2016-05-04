using Ramulator.MemCtrl;
using Ramulator.MemReq;
using System.Collections.Generic;

namespace Ramulator.MemSched
{
    public abstract class MemSched
    {
        public MemCtrl.MemCtrl Mctrl;
        public MemCtrl.MemCtrl[] Mctrls;

        public uint LocalBcount;
        public uint GlobalBcount;

        public MemSched()
        {
        }

        public MemSched(MemCtrl.MemCtrl mctrl, MemCtrl.MemCtrl[] mctrls)
        {
            Mctrl = mctrl;
            Mctrls = mctrls;

            LocalBcount = mctrl.Rmax * mctrl.Bmax;
            GlobalBcount = (uint)mctrls.Length * LocalBcount;
        }

        public virtual void Initialize()
        {
        }

        public virtual void issue_req(Req req)
        {
        }

        public abstract void dequeue_req(Req req);

        public abstract void enqueue_req(Req req);

        //scheduler-specific overridden method
        public abstract Req better_req(Req req1, Req req2);

        public virtual void Tick()
        {
        }

        public bool is_row_hit(Req req)
        {
            return Mctrl.is_row_hit(req);
        }

        public Cmd decode_cmd(Req req)
        {
            return Mctrl.Dstate.Crack(req);
        }

        public uint get_local_boffset(Req req)
        {
            return (req.Addr.rid * Mctrl.Bmax) + req.Addr.bid;
        }

        public uint get_global_boffset(Req req)
        {
            return (req.Addr.cid * LocalBcount) + get_local_boffset(req);
        }

        public virtual Req find_best_req(List<Req> q)
        {
            if (q.Count == 0)
                return null;

            Req bestReq = q[0];
            for (int i = 1; i < q.Count; i++)
            {
                bestReq = better_req(bestReq, q[i]);
            }
            return bestReq;
        }
    }
}