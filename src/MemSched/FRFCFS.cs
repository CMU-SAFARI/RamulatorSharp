using Ramulator.MemReq;
using Ramulator.Sim;

namespace Ramulator.MemSched
{
    public class FRFCFS : MemSched
    {
        public FRFCFS(MemCtrl.MemCtrl mctrl, MemCtrl.MemCtrl[] mctrls)
            : base(mctrl, mctrls)
        {
        }

        public override void enqueue_req(Req req)
        {
        }

        public override void dequeue_req(Req req)
        {
        }

        public override Req better_req(Req req1, Req req2)
        {
            bool hit1 = is_row_hit(req1);
            bool hit2 = is_row_hit(req2);
            if (hit1 ^ hit2)
            {
                if (hit1) return req1;
                else return req2;
            }
            if (req1.TsArrival <= req2.TsArrival) return req1;
            else return req2;
        }
    }

    // Prioritize the one at the head of ROB
    public class FHFRFCFS : MemSched
    {
        public FHFRFCFS(MemCtrl.MemCtrl mctrl, MemCtrl.MemCtrl[] mctrls)
            : base(mctrl, mctrls)
        {
        }

        public override void enqueue_req(Req req)
        {
        }

        public override void dequeue_req(Req req)
        {
        }

        public override Req better_req(Req req1, Req req2)
        {
            bool r1_head = (Sim.Sim.Procs[req1.Pid].inst_wnd.head() == req1.BlockAddr);
            bool r2_head = (Sim.Sim.Procs[req2.Pid].inst_wnd.head() == req2.BlockAddr);

            if (r1_head ^ r2_head)
            {
                if (r1_head) return req1;
                else return req2;
            }
            else if (r1_head && r2_head)
            {
                if (req1.TsArrival <= req2.TsArrival) return req1;
                else return req2;
            }

            bool hit1 = is_row_hit(req1);
            bool hit2 = is_row_hit(req2);
            if (hit1 ^ hit2)
            {
                if (hit1) return req1;
                else return req2;
            }
            if (req1.TsArrival <= req2.TsArrival) return req1;
            else return req2;
        }
    }

    public class FRFCFS_CAP : MemSched
    {
        public FRFCFS_CAP(MemCtrl.MemCtrl mctrl, MemCtrl.MemCtrl[] mctrls)
            : base(mctrl, mctrls)
        {
        }

        // streak
        private int[] streak;

        public override void Initialize()
        {
            streak = new int[LocalBcount];
        }

        public override void enqueue_req(Req req)
        {
        }

        public override void dequeue_req(Req req)
        {
        }

        public override Req better_req(Req req1, Req req2)
        {
            bool hit1 = is_row_hit(req1);
            bool hit2 = is_row_hit(req2);

            uint bid1 = get_local_boffset(req1);
            uint bid2 = get_local_boffset(req2);
            bool capped1 = streak[bid1] >= Config.sched.row_hit_cap;
            bool capped2 = streak[bid2] >= Config.sched.row_hit_cap;

            hit1 = hit1 && (!capped1);
            hit2 = hit2 && (!capped2);

            if (hit1 ^ hit2)
            {
                if (hit1) return req1;
                else return req2;
            }
            if (req1.TsArrival <= req2.TsArrival) return req1;
            else return req2;
        }

        public override void issue_req(Req req)
        {
            if (req != null)
            {
                uint bid = get_local_boffset(req);

                if (!req.RequiredActivate) streak[bid] += 1;
                else streak[bid] = 1;
            }
        }
    }

    public class FRFCFS_QoS : MemSched
    {
        public FRFCFS_QoS(MemCtrl.MemCtrl mctrl, MemCtrl.MemCtrl[] mctrls)
            : base(mctrl, mctrls)
        {
        }

        public override void enqueue_req(Req req)
        {
        }

        public override void dequeue_req(Req req)
        {
        }

        public override Req better_req(Req req1, Req req2)
        {
            if (Mctrl.Cycles - req1.TsArrival > Config.sched.frfcfs_qos_threshold)
                return req1;
            else if (Mctrl.Cycles - req2.TsArrival > Config.sched.frfcfs_qos_threshold)
                return req2;

            bool hit1 = is_row_hit(req1);
            bool hit2 = is_row_hit(req2);
            if (hit1 ^ hit2)
            {
                if (hit1) return req1;
                else return req2;
            }
            if (req1.TsArrival <= req2.TsArrival) return req1;
            else return req2;
        }
    }

    public class FRFCFS_SACAP : MemSched
    {
        // streak per subarray
        private int[, ,] streak;

        public FRFCFS_SACAP(MemCtrl.MemCtrl mctrl, MemCtrl.MemCtrl[] mctrls)
            : base(mctrl, mctrls)
        {
            streak = new int[mctrl.Rmax, mctrl.Bmax, mctrl.ddr3.SUBARRAYS_PER_BANK];
            for (int r = 0; r < mctrl.Rmax; r++)
                for (int b = 0; b < mctrl.Bmax; b++)
                    for (int s = 0; s < mctrl.ddr3.SUBARRAYS_PER_BANK; s++)
                        streak[r, b, s] = 0;
        }

        public override void enqueue_req(Req req)
        {
        }

        public override void dequeue_req(Req req)
        {
        }

        public override Req better_req(Req req1, Req req2)
        {
            bool hit1 = is_row_hit(req1);
            bool hit2 = is_row_hit(req2);

            bool capped1 = streak[req1.Addr.rid, req1.Addr.bid, req1.Addr.said] >= Config.sched.row_hit_cap;
            bool capped2 = streak[req2.Addr.rid, req2.Addr.bid, req2.Addr.said] >= Config.sched.row_hit_cap;

            hit1 = hit1 && (!capped1);
            hit2 = hit2 && (!capped2);

            if (hit1 ^ hit2)
            {
                if (hit1) return req1;
                else return req2;
            }
            if (req1.TsArrival <= req2.TsArrival) return req1;
            else return req2;
        }

        public override void issue_req(Req req)
        {
            if (req != null)
            {
                if (!req.RequiredActivate)
                    streak[req.Addr.rid, req.Addr.bid, req.Addr.said] += 1;
                else
                    streak[req.Addr.rid, req.Addr.bid, req.Addr.said] = 0;
            }
        }
    }
}