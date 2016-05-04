using Ramulator.MemReq;
using Ramulator.Sim;

namespace Ramulator.MemSched
{
    public class BLISS : MemSched
    {
        //shuffle
        private int _shuffleCyclesLeft;

        private int[] _mark;
        private int _lastReqPid;
        private int _oldestStreakGlobal;

        public BLISS(MemCtrl.MemCtrl mctrl, MemCtrl.MemCtrl[] mctrls)
            : base(mctrl, mctrls)
        {
            _shuffleCyclesLeft = Config.sched.bliss_shuffle_cycles;
            _mark = new int[Config.N];
        }

        public override void Initialize()
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
            if (_mark[req1.Pid] != 1 ^ _mark[req2.Pid] != 1)
            {
                if (_mark[req1.Pid] != 1)
                    return req1;
                else
                    return req2;
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

        public override void Tick()
        {
            base.Tick();

            //shuffle
            if (_shuffleCyclesLeft > 0)
            {
                _shuffleCyclesLeft--;
            }
            else
            {
                _shuffleCyclesLeft = Config.sched.bliss_shuffle_cycles;
                clear_marking();
            }
        }

        public void clear_marking()
        {
            for (int p = 0; p < Config.N; p++)
                _mark[p] = 0;
        }

        public override void issue_req(Req req)
        {
            if (req == null) return;

            // Channel-level bliss
            {
                if (req.Pid == _lastReqPid &&
                    _oldestStreakGlobal < Config.sched.bliss_row_hit_cap)
                {
                    _oldestStreakGlobal += 1;
                }
                else if (req.Pid == _lastReqPid &&
                         _oldestStreakGlobal == Config.sched.bliss_row_hit_cap)
                {
                    _mark[req.Pid] = 1;
                    _oldestStreakGlobal = 1;
                }
                else
                {
                    _oldestStreakGlobal = 1;
                }
                _lastReqPid = req.Pid;
            }
        }
    }
}