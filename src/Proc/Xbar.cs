using Ramulator.MemCtrl;
using Ramulator.MemReq;
using System.Collections.Generic;
using Ramulator.Sim;

namespace Ramulator.Proc
{
    public class Xbar
    {
        public long Cycles;
        public List<Req> Reqs;

        public Xbar()
        {
            Reqs = new List<Req>(128);
        }

        public void Enqueue(Req req)
        {
            //stats
            req.TsDeparture = Cycles;
            req.Latency = (int)(req.TsDeparture - req.TsArrival);
            //enqueue proper
            Reqs.Add(req);
        }

        public void Tick()
        {
            Cycles++;

            int sent = 0;
            foreach (Req req in Reqs)
            {
                if (Cycles - req.TsDeparture < Config.mctrl.xbar_latency) break;

                // Send back to processor
                sent += 1;
                Callback cb = req.Callback;
                cb(req);
            }
            Reqs.RemoveRange(0, sent);
        }
    }
}