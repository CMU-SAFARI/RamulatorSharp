using Ramulator.MemReq;
using System;
using System.Collections.Generic;
using Ramulator.Sim;

namespace Ramulator.MemSched
{
    public class ATLAS : MemSched
    {
        private int[] _rank;

        // attained service
        private uint[] _serviceBankCnt;

        private double[] _currService;
        private double[] _service;

        // quantum
        private int _quantumCyclesLeft;

        public ATLAS(MemCtrl.MemCtrl mctrl, MemCtrl.MemCtrl[] mctrls)
            : base(mctrl, mctrls)
        {
            _rank = new int[Config.N];
            _serviceBankCnt = new uint[Config.N];
            _currService = new double[Config.N];
            _service = new double[Config.N];

            _quantumCyclesLeft = Config.sched.quantum_cycles;
        }

        public override void enqueue_req(Req req)
        {
        }

        public override void dequeue_req(Req req)
        {
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

            int rank1 = _rank[req1.Pid];
            int rank2 = _rank[req2.Pid];
            if (rank1 != rank2)
            {
                if (rank1 > rank2) return req1;
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

        public override void Tick()
        {
            base.Tick();

            increment_service();
            mark_old_requests();

            if (_quantumCyclesLeft > 0)
            {
                _quantumCyclesLeft--;
                return;
            }

            // new quantum
            _quantumCyclesLeft = Config.sched.quantum_cycles;
            decay_service();
            assign_rank();
        }

        private void increment_service()
        {
            for (int p = 0; p < Config.N; p++)
                _serviceBankCnt[p] = 0;

            // count requests
            foreach (MemCtrl.MemCtrl m in Mctrls)
            {
                foreach (List<Req> q in m.Inflightqs)
                {
                    foreach (Req r in q)
                    {
                        _serviceBankCnt[r.Pid]++;
                    }
                }
            }

            // update service
            for (int p = 0; p < Config.N; p++)
            {
                _currService[p] += _serviceBankCnt[p];
            }
        }

        private void mark_old_requests()
        {
            foreach (MemCtrl.MemCtrl m in Mctrls)
            {
                foreach (List<Req> readQ in m.Readqs)
                {
                    foreach (Req req in readQ)
                    {
                        if (m.Cycles - req.TsArrival > Config.sched.threshold_cycles)
                        {
                            req.Marked = true;
                        }
                    }
                }
            }
        }

        private void decay_service()
        {
            for (int p = 0; p < Config.N; p++)
            {
                if (Config.sched.use_weights != 0)
                {
                    _currService[p] = _currService[p] / Config.sched.weights[p];
                }

                _service[p] = Config.sched.history_weight * _service[p] + (1 - Config.sched.history_weight) * _currService[p];
                _currService[p] = 0;
            }
        }

        private void assign_rank()
        {
            int[] tids = new int[Config.N];
            for (int p = 0; p < Config.N; p++)
                tids[p] = p;

            Array.Sort(tids, sort);
            for (int p = 0; p < Config.N; p++)
            {
                _rank[p] = Array.IndexOf(tids, p);
            }
        }

        private int sort(int tid1, int tid2)
        {
            //return 1 if first argument is "greater" (higher rank)
            if (Math.Abs(_service[tid1] - _service[tid2]) > 0)
            {
                if (_service[tid1] < _service[tid2]) return 1;
                else return -1;
            }
            return 0;
        }
    }
}