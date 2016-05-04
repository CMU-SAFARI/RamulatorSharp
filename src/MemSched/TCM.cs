using Ramulator.MemReq;
using Ramulator.Sim;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Ramulator.MemSched
{
    public class TCM : MemSched
    {
        //rank
        private int[] _rank;

        //attained service
        private double[] _service;

        private double[] _currService;
        private uint[] _serviceBankCnt;

        //mpki
        private double[] _mpki;

        private ulong[] _prevCacheMiss;
        private ulong[] _prevInstCnt;

        //rbl
        private double[] _rbl;

        private ulong[] _shadowRowHits;
        private double _rblDiff;

        //blp
        private double[] _blp;

        private uint[] _blpSampleSum;
        private uint _blpSampleCnt;
        private double _blpDiff;

        //quantum
        private int _quantumCnt;

        private int _quantumCyclesLeft;

        //shuffle
        private readonly int[] _nice;

        private int _shuffleCnt;
        private int _shuffleCyclesLeft;

        //shuffle
        private Random _rand = new Random(0);

        public enum ShuffleAlgo
        {
            Naive,
            Random,
            Hanoi,
            ControlledRandom
        }

        //cluster sizes
        private int _iclusterSize;

        public TCM(MemCtrl.MemCtrl mctrl, MemCtrl.MemCtrl[] mctrls)
            : base(mctrl, mctrls)
        {
            _rank = new int[Config.N];

            _service = new double[Config.N];
            _currService = new double[Config.N];
            _serviceBankCnt = new uint[Config.N];

            _mpki = new double[Config.N];
            _prevCacheMiss = new ulong[Config.N];
            _prevInstCnt = new ulong[Config.N];

            _rbl = new double[Config.N];
            _shadowRowHits = new ulong[Config.N];

            _blp = new double[Config.N];
            _blpSampleSum = new uint[Config.N];

            _quantumCyclesLeft = Config.sched.quantum_cycles;

            _nice = new int[Config.N];
            _shuffleCyclesLeft = Config.sched.shuffle_cycles;
        }

        public override void enqueue_req(Req req)
        {
        }

        public override void dequeue_req(Req req)
        {
        }

        public override Req better_req(Req req1, Req req2)
        {
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

            //service
            increment_service();

            //blp
            if (Mctrl.Cycles % 1000 == 0)
            {
                sample_blp();
            }

            //shuffle
            if (_shuffleCyclesLeft > 0)
            {
                _shuffleCyclesLeft--;
            }
            else if (_quantumCnt != 0 && _iclusterSize > 1)
            {
                Shuffle();
                _shuffleCnt++;
                _shuffleCyclesLeft = Config.sched.shuffle_cycles;
            }

            //quantum
            if (_quantumCyclesLeft > 0)
            {
                _quantumCyclesLeft--;
                return;
            }

            //new quantum
            decay_stats();

            _quantumCnt++;
            _quantumCyclesLeft = Config.sched.quantum_cycles;

            _shuffleCnt = 0;
            _shuffleCyclesLeft = Config.sched.shuffle_cycles;

            //cluster
            _iclusterSize = Cluster();
            if (_iclusterSize > 1) assign_nice_rank();
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

        private void sample_blp()
        {
            _blpSampleCnt++;

            for (uint p = 0; p < Config.N; p++)
            {
                uint currBlp = 0;

                foreach (MemCtrl.MemCtrl m in Mctrls)
                {
                    for (uint r = 0; r < Mctrl.Rmax; r++)
                    {
                        for (uint b = 0; b < Mctrl.Bmax; b++)
                        {
                            if (m.RloadPerProcrankbank[p, r, b] > 0)
                            {
                                currBlp++;
                            }
                        }
                    }
                }

                _blpSampleSum[p] += currBlp;
            }
        }

        private void decay_stats()
        {
            for (int p = 0; p < Config.N; p++)
            {
                ulong cacheMiss;
                cacheMiss = Config.sched.tcm_only_rmpki ? Sim.Sim.Procs[p].mem_rd_req_count : Sim.Sim.Procs[p].mem_req_count;

                ulong deltaCacheMiss = cacheMiss - _prevCacheMiss[p];
                _prevCacheMiss[p] = cacheMiss;

                ulong instCnt = Sim.Sim.Procs[p].inst_count;
                ulong deltaInstCnt = instCnt - _prevInstCnt[p];
                _prevInstCnt[p] = instCnt;

                //mpki
                double currMpki = 1000 * ((double)deltaCacheMiss) / deltaInstCnt;
                _mpki[p] = Config.sched.history_weight * _mpki[p] + (1 - Config.sched.history_weight) * currMpki;

                //rbl
                double currRbl = ((double)_shadowRowHits[p]) / deltaCacheMiss;
                _rbl[p] = Config.sched.history_weight * _rbl[p] + (1 - Config.sched.history_weight) * currRbl;
                _shadowRowHits[p] = 0;

                //blp
                double currBlp = ((double)_blpSampleSum[p]) / _blpSampleCnt;
                _blp[p] = Config.sched.history_weight * _blp[p] + (1 - Config.sched.history_weight) * currBlp;
                _blpSampleSum[p] = 0;

                //service
                _service[p] = _currService[p];
                _currService[p] = 0;
            }
            _blpSampleCnt = 0;
        }

        private int Cluster()
        {
            //rank
            int[] tids = new int[Config.N];
            for (int p = 0; p < Config.N; p++)
                tids[p] = p;

            Array.Sort(tids, sort_mpki);
            for (int p = 0; p < Config.N; p++)
            {
                _rank[p] = Array.IndexOf(tids, p);
            }

            //cluster
            int nclusterSize = 0;
            double serviceTotal = 0;
            double serviceRunsum = 0;

            for (int p = 0; p < Config.N; p++)
                serviceTotal += _service[p];

            for (int r = Config.N - 1; r >= 0; r--)
            {
                int pid = Array.IndexOf(_rank, r);
                serviceRunsum += _service[pid];
                if (serviceRunsum > Config.sched.AS_cluster_factor * serviceTotal)
                    break;

                nclusterSize++;
            }

            return Config.N - nclusterSize;
        }

        private void Shuffle()
        {
            ShuffleAlgo shuffleAlgo = Config.sched.shuffle_algo;
            if (Config.sched.is_adaptive_shuffle)
            {
                double blpThresh = Config.sched.adaptive_threshold * GlobalBcount;
                double rblThresh = Config.sched.adaptive_threshold;
                if (_blpDiff > blpThresh && _rblDiff > rblThresh)
                {
                    shuffleAlgo = ShuffleAlgo.Hanoi;
                }
                else
                {
                    shuffleAlgo = ShuffleAlgo.ControlledRandom;
                }
            }

            //rank_to_pid translation
            int[] pids = new int[Config.N];
            for (int p = 0; p < Config.N; p++)
            {
                int r = _rank[p];
                pids[r] = p;
            }

            //shuffle proper
            switch (shuffleAlgo)
            {
                case ShuffleAlgo.Naive:
                    for (int r = 0; r < _iclusterSize; r++)
                    {
                        int pid = pids[r];
                        _rank[pid] = (r + (_iclusterSize - 1)) % _iclusterSize;
                    }
                    break;

                case ShuffleAlgo.ControlledRandom:
                    int step = _iclusterSize / 2 + 1;
                    for (int r = 0; r < _iclusterSize; r++)
                    {
                        int pid = pids[r];
                        _rank[pid] = (r + step) % _iclusterSize;
                    }
                    break;

                case ShuffleAlgo.Random:
                    for (int r = _iclusterSize - 1; r > 0; r--)
                    {
                        int pid1 = Array.IndexOf(_rank, r);

                        int chosenR = _rand.Next(r + 1);
                        int chosenPid = Array.IndexOf(_rank, chosenR);

                        _rank[pid1] = chosenR;
                        _rank[chosenPid] = r;
                    }
                    break;

                case ShuffleAlgo.Hanoi:
                    int even = 2 * _iclusterSize;
                    int phase = _shuffleCnt % even;

                    if (phase < _iclusterSize)
                    {
                        int grabRank = (_iclusterSize - 1) - phase;
                        int grabPid = Array.IndexOf(_rank, grabRank);
                        _rank[grabPid] = -1;

                        for (int r = grabRank + 1; r <= _iclusterSize - 1; r++)
                        {
                            int pid = Array.IndexOf(_rank, r);
                            _rank[pid] = r - 1;
                        }
                        _rank[grabPid] = _iclusterSize - 1;
                    }
                    else
                    {
                        int grabRank = (_iclusterSize - 1);
                        int grabPid = Array.IndexOf(_rank, grabRank);
                        _rank[grabPid] = -1;

                        for (int r = grabRank - 1; r >= (phase - 1) % _iclusterSize; r--)
                        {
                            int pid = Array.IndexOf(_rank, r);
                            _rank[pid] = r + 1;
                        }
                        _rank[grabPid] = (phase - 1) % _iclusterSize;
                    }
                    break;
            }

            //sanity check
            for (int r = 0; r < Config.N; r++)
            {
                int pid = Array.IndexOf(_rank, r);
                Dbg.Assert(pid != -1);
            }
        }

        private void assign_nice_rank()
        {
            int[] iclusterPids = new int[_iclusterSize];
            for (int r = 0; r < iclusterPids.Length; r++)
            {
                iclusterPids[r] = Array.IndexOf(_rank, r);
            }

            int[] pids = new int[_iclusterSize];

            //blp rank
            Array.Copy(iclusterPids, pids, _iclusterSize);
            int[] blpRank = new int[Config.N];
            Array.Sort(pids, sort_blp);
            for (int r = 0; r < pids.Length; r++)
            {
                int pid = pids[r];
                blpRank[pid] = r;
            }
            _blpDiff = _blp.Max() - _blp.Min();

            //rbl rank
            Array.Copy(iclusterPids, pids, _iclusterSize);
            int[] rblRank = new int[Config.N];
            Array.Sort(pids, sort_rbl);
            for (int r = 0; r < pids.Length; r++)
            {
                int pid = pids[r];
                rblRank[pid] = r;
            }
            _rblDiff = _rbl.Max() - _rbl.Min();

            //nice
            Array.Clear(_nice, 0, _nice.Length);
            foreach (int pid in iclusterPids)
            {
                _nice[pid] = blpRank[pid] - rblRank[pid];
            }

            //nice rank
            Array.Copy(iclusterPids, pids, _iclusterSize);
            int[] niceRank = new int[Config.N];
            Array.Sort(pids, sort_nice);
            for (int r = 0; r < pids.Length; r++)
            {
                int pid = pids[r];
                niceRank[pid] = r;
            }

            //copy
            foreach (int pid in iclusterPids)
            {
                _rank[pid] = niceRank[pid];
            }

            //sanity check
            for (int r = 0; r < Config.N; r++)
            {
                int pid = Array.IndexOf(_rank, r);
                Dbg.Assert(pid != -1);
            }
        }

        private int sort_mpki(int pid1, int pid2)
        {
            //return 1 if first argument is "greater" (higher rank)
            if (pid1 == pid2) return 0;

            double mpki1 = _mpki[pid1];
            double mpki2 = _mpki[pid2];

            if (mpki1 < mpki2) return 1;
            else return -1;
        }

        private int sort_rbl(int pid1, int pid2)
        {
            //return 1 if first argument is "greater" (higher rank)
            if (pid1 == pid2) return 0;

            double rbl1 = _rbl[pid1];
            double rbl2 = _rbl[pid2];

            if (rbl1 < rbl2) return 1;
            else return -1;
        }

        private int sort_blp(int pid1, int pid2)
        {
            //return 1 if first argument is "greater" (higher rank)
            if (pid1 == pid2) return 0;

            double blp1 = _blp[pid1];
            double blp2 = _blp[pid2];

            if (blp1 > blp2) return 1;
            else return -1;
        }

        private int sort_nice(int pid1, int pid2)
        {
            //return 1 if first argument is "greater" (higher rank)
            int nice1 = _nice[pid1];
            int nice2 = _nice[pid2];

            if (nice1 != nice2)
            {
                if (nice1 > nice2) return 1;
                else return -1;
            }
            return 0;
        }

        public override void issue_req(Req req)
        {
            if (req == null) return;

            ulong shadowRowid = Mctrl.ShadowRowidPerProcrankbank[req.Pid, req.Addr.rid, req.Addr.bid];
            if (shadowRowid == req.Addr.rowid)
            {
                _shadowRowHits[req.Pid]++;
            }
        }
    }
}