using Ramulator.MemReq;

namespace Ramulator.MemSched
{
    public class FCFS : MemSched
    {
        public FCFS(MemCtrl.MemCtrl mctrl, MemCtrl.MemCtrl[] mctrls)
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
            if (req1.TsArrival <= req2.TsArrival) return req1;
            else return req2;
        }
    }
}