using Ramulator.MemReq;
using System;
using System.Collections.Generic;
using Ramulator.Sim;

namespace Ramulator.MemSched
{
    public class PARBS : MemSched
    {
        private int[] _rank;

        // batch
        private uint _markedLoad;

        private uint[] _markedMaxLoadPerProc;
        private uint[] _markedTotalLoadPerProc;
        private List<Req>[,] _markableQ;

        public PARBS(MemCtrl.MemCtrl mctrl, MemCtrl.MemCtrl[] mctrls)
            : base(mctrl, mctrls)
        {
            _rank = new int[Config.N];
            _markedMaxLoadPerProc = new uint[Config.N];
            _markedTotalLoadPerProc = new uint[Config.N];
        }

        public override void Initialize()
        {
            _markableQ = new List<Req>[Config.N, LocalBcount];
            for (int p = 0; p < Config.N; p++)
            {
                for (int b = 0; b < LocalBcount; b++)
                {
                    _markableQ[p, b] = new List<Req>(Config.sched.batch_cap);
                }
            }
        }

        public override void enqueue_req(Req req)
        {
        }

        public override void dequeue_req(Req req)
        {
            if (!req.Marked)
                return;

            Dbg.Assert(_markedLoad > 0);
            _markedLoad--;
        }

        public override Req better_req(Req req1, Req req2)
        {
            bool marked1 = req1.Marked;
            bool marked2 = req2.Marked;
            if (marked1 ^ marked2)
            {
                if (marked1) return req1;
                else return req2;
            }

            bool hit1 = is_row_hit(req1);
            bool hit2 = is_row_hit(req2);
            if (hit1 ^ hit2)
            {
                if (hit1) return req1;
                else return req2;
            }

            int rank1 = _rank[req1.Pid];
            int rank2 = _rank[req2.Pid];
            if (rank1 != rank2)
            {
                if (rank1 > rank2) return req1;
                else return req2;
            }

            if (req1.TsArrival <= req2.TsArrival) return req1;
            else return req2;
        }

        public override void Tick()
        {
            base.Tick();

            if (_markedLoad > 0 || Mctrl.Rload < 3)
                return;

            //new batch
            form_batch();
            assign_rank();
        }

        private void form_batch()
        {
            // initialization
            for (int b = 0; b < LocalBcount; b++)
            {
                for (int p = 0; p < Config.N; p++)
                {
                    _markableQ[p, b].Clear();
                    _markedMaxLoadPerProc[p] = 0;
                    _markedTotalLoadPerProc[p] = 0;
                }
            }

            // demultiplex request buffer into separate processors
            uint bcount = 0;
            foreach (List<Req> q in Mctrl.Readqs)
            {
                foreach (Req req in q)
                {
                    Dbg.Assert(!req.Marked);
                    int p = req.Pid;
                    _markableQ[p, bcount].Add(req);
                }

                bcount++;
            }

            // find earliest arriving requests for each processor at each bank
            for (uint b = 0; b < LocalBcount; b++)
            {
                for (int p = 0; p < Config.N; p++)
                {
                    _markableQ[p, b].Sort((req1, req2) => req1.TsArrival.CompareTo(req2.TsArrival));
                }
            }

            // mark requests
            for (int p = 0; p < Config.N; p++)
            {
                for (int b = 0; b < LocalBcount; b++)
                {
                    List<Req> q = _markableQ[p, b];
                    uint markedCnt = 0;
                    foreach (Req req in q)
                    {
                        if (markedCnt == Config.sched.batch_cap)
                            break;
                        req.Marked = true;
                        markedCnt++;
                    }

                    _markedLoad += markedCnt;
                    _markedTotalLoadPerProc[p] += markedCnt;
                    if (markedCnt > _markedMaxLoadPerProc[p])
                        _markedMaxLoadPerProc[p] = markedCnt;
                }
            }
        }

        private void assign_rank()
        {
            int[] tids = new int[Config.N];
            for (int p = 0; p < Config.N; p++)
                tids[p] = p;

            Array.Sort(tids, sort_maxtot);
            for (int p = 0; p < Config.N; p++)
            {
                _rank[p] = Array.IndexOf(tids, p);
            }
        }

        private int sort_maxtot(int tid1, int tid2)
        {
            // return 1 if first argument is "greater" (higher rank)
            uint max1 = _markedMaxLoadPerProc[tid1];
            uint max2 = _markedMaxLoadPerProc[tid2];
            uint tot1 = _markedTotalLoadPerProc[tid1];
            uint tot2 = _markedTotalLoadPerProc[tid2];

            if (max1 != max2)
            {
                if (max1 < max2) return 1;
                else return -1;
            }

            if (tot1 != tot2)
            {
                if (tot1 < tot2) return 1;
                else return -1;
            }

            return 0;
        }
    }
}