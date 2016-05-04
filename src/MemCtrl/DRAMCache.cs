using Ramulator.Mem;
using Ramulator.MemReq;
using Ramulator.Proc;
using System;
using System.Collections.Generic;
using System.Linq;
using Ramulator.Sim;

namespace Ramulator.MemCtrl
{
    /* Implementation based on Lee et al., TL-DRAM, HPCA 2013 */

    public class DRAMCache : Cache
    {
        private uint _cid;
        private uint _rid;
        private uint _bid;

        // TL-DRAM BBC
        public List<int> BBCCounter;

        public DRAMCache(uint cacheSize, uint cacheAssoc, uint cacheBlockSize, uint hitLatency, uint cid, uint rid, uint bid) :
            base(cacheSize, cacheAssoc, cacheBlockSize, hitLatency, -1, false)
        {
            _cid = cid;
            _rid = rid;
            _bid = bid;

            BBCCounter = new List<int>();
            for (int i = 0; i < cacheAssoc; i++)
                BBCCounter.Add(0);
        }

        public override void CacheStats(ReqType req_type, bool hit, ulong block_addr = 0)
        {
            if (!hit)
            {
                Stat.banks[_cid, _rid, _bid].villa_hit_rate.collect(0);
                Stat.banks[_cid, _rid, _bid].villa_misses.collect();
            }
            else
            {
                Stat.banks[_cid, _rid, _bid].villa_hit_rate.collect(1);
                Stat.banks[_cid, _rid, _bid].villa_hits.collect();
            }
        }

        public override ulong cache_add(ulong block_addr, ReqType inst_type, int pid)
        {
            if (Config.mctrl.villa_lru)
                return base.cache_add(block_addr, inst_type, pid);
            //calculate set index
            int setIndex = (int)(block_addr % SetMax);

            //empty entry within a set
            int emptyEntryIndex = -1;

            //lru entry within a set
            int lruEntryIndex = -1;

            //search for empty or lru entry
            for (int i = 0; i < Assoc; i++)
            {
                //make sure not already in cache
                Dbg.Assert(cache[setIndex, i] != block_addr);
                if (cache[setIndex, i] == Proc.Proc.NULL_ADDRESS)
                {
                    emptyEntryIndex = i;
                    break;
                }
            }

            // Replace least benefit
            if (emptyEntryIndex == -1)
            {
                lruEntryIndex = BBCCounter.IndexOf(BBCCounter.Min());
                BBCCounter[lruEntryIndex] = 0;
                // Halve benefit
                for (int i = 0; i < Assoc; i++)
                    BBCCounter[i] /= 2;
            }
            else
                BBCCounter[emptyEntryIndex]++;

            ulong returnAddr = Proc.Proc.NULL_ADDRESS;
            int replaceAssocIdx = (emptyEntryIndex != -1) ?
                emptyEntryIndex : lruEntryIndex;

            // Add the new block
            cache[setIndex, replaceAssocIdx] = block_addr;
            Dirty[setIndex, replaceAssocIdx] = (inst_type == ReqType.WRITE);
            CoreId[setIndex, replaceAssocIdx] = pid; // for partitioning
            return returnAddr;
        }

        public override bool is_cache_hit(ulong block_addr, ReqType inst_type)
        {
            if (Config.mctrl.villa_lru)
                return base.is_cache_hit(block_addr, inst_type);

            //calculate set index
            int setIndex = (int)(block_addr % SetMax);

            //search for block
            for (int i = 0; i < Assoc; i++)
            {
                if (cache[setIndex, i] == block_addr)
                {
                    Hit++;
                    BBCCounter[i]++;
                    if (inst_type == ReqType.WRITE)
                    {
                        Dirty[setIndex, i] = true;
                    }
                    CacheStats(inst_type, true);
                    return true;
                }
            }

            // Couldn't find block_addr; miss
            Miss++;
            CacheStats(inst_type, false);
            return false;
        }
    }

    /* Caching policy based on Yoon et al., RBLA, ICCD 2012 */

    public class RBLA_Monitor
    {
        private int _numCopy;
        private readonly int migration_cost;
        private int _act;
        private int _actGain;
        private int _pre;
        private int _preGain;
        private MemCtrl _mctrl;
        private int _prevNetBenefit;
        private bool _prevAdjust;

        public RBLA_Monitor(MemCtrl mctrl, DDR3DRAM.Timing tc, DDR3DRAM.Timing old_tc)
        {
            _mctrl = mctrl;
            migration_cost = (int)tc.tLISA_INTER_SA_COPY;
            _act = 0;
            _actGain = (int)(old_tc.tRCD - tc.tRCD);
            _pre = 0;
            _preGain = (int)(old_tc.tRP - tc.tRP);
            _prevNetBenefit = 0;
            _prevAdjust = false;
        }

        public void Migrate()
        {
            _numCopy++;
        }

        public void Hit(Cmd cmd)
        {
            if (cmd.Type == CmdType.ACT)
                _act++;
            else if (cmd.Type == CmdType.PRE_BANK)
                _pre++;
        }

        public void calc_benefit()
        {
            int cost = _numCopy * migration_cost;
            int benefit = _act * _actGain + _pre * _preGain;
            int netBenefit = benefit - cost;

            // Adjust
            int adjust = 0;
            int adjustStep = Config.mctrl.rbla_adjust_step;
            if (netBenefit < 0)
                adjust += adjustStep;
            else if (netBenefit >= _prevNetBenefit)
            {
                if (_prevAdjust)
                    adjust += adjustStep;
                else
                    adjust -= adjustStep;
            }
            else
            {
                if (_prevAdjust)
                    adjust -= adjustStep;
                else
                    adjust += adjustStep;
            }

            if (adjust != 0)
            {
                if (Config.mctrl.villa_cache_method == VILLA_HOT.RBLA)
                {
                    for (int r = 0; r < _mctrl.Rmax; r++)
                        for (int b = 0; b < _mctrl.Bmax; b++)
                            _mctrl.DramCacheStats[r, b].adjust_thresh(adjust);
#if DEBUG
                    Console.WriteLine("Net Benefit {0} adjust {1} threshold {2}", netBenefit, adjust, _mctrl.DramCacheStats[0, 0].RbCacheThreshold);
#endif
                }
                else if (Config.mctrl.villa_cache_method == VILLA_HOT.EPOCH)
                {
                    if (adjust < 0)
                        adjust *= 2; // faster decrease
                    Config.mctrl.memcache_hot_hit_thresh += adjust;
                    if (Config.mctrl.memcache_hot_hit_thresh < 0)
                        Config.mctrl.memcache_hot_hit_thresh = 0;
#if DEBUG
                    Console.WriteLine("Net Benefit {0} adjust {1} threshold {2}", netBenefit, adjust, Config.mctrl.memcache_hot_hit_thresh);
#endif
                }
            }

            _act = 0;
            _pre = 0;
            _numCopy = 0;
            _prevNetBenefit = netBenefit;
            _prevAdjust = (adjust > 0); // adjusted?
        }
    }

    public class RBLA_Stats : Cache
    {
        public struct RblaCounter
        {
            public int Benefit;
            public int ColumnStreak;
        }

        // RBLA benefits -- this is really a counter per way
        public RblaCounter[,] rb_BBC;

        private int _trasColumnLen;
        public int RbCacheThreshold;

        public RBLA_Stats(uint cacheSize, uint cacheAssoc, uint cacheBlockSize, uint hitLatency, uint cid, uint rid, uint bid, DDR3DRAM.Timing slow_tc) :
            base(cacheSize, cacheAssoc, cacheBlockSize, hitLatency, -1, false)
        {
            rb_BBC = new RblaCounter[SetMax, cacheAssoc];

            for (int i = 0; i < SetMax; i++)
                for (int j = 0; j < Assoc; j++)
                {
                    rb_BBC[i, j].Benefit = 0;
                    rb_BBC[i, j].ColumnStreak = 0;
                }

            // Number of column commands needed to outweigh tRAS
            _trasColumnLen = (int)Math.Floor(((double)(slow_tc.tRAS - slow_tc.tCL)) / slow_tc.tCCD);
            RbCacheThreshold = Config.mctrl.rbla_cache_threshold;
        }

        public void clean_history()
        {
            for (int i = 0; i < SetMax; i++)
                for (int j = 0; j < Assoc; j++)
                {
                    rb_BBC[i, j].Benefit /= 2;
                    rb_BBC[i, j].ColumnStreak = 0;
                }
        }

        public override void CacheStats(ReqType req_type, bool hit, ulong block_addr = 0)
        {
        }

        public void cache_add(ulong block_addr, Cmd cmd)
        {
            //calculate set index
            int setIndex = (int)(block_addr % SetMax);

            //empty entry within a set
            int emptyEntryIndex = -1;

            //lru entry within a set
            int lruEntryIndex = -1;

            //search for empty or lru entry
            for (int i = 0; i < Assoc; i++)
            {
                //make sure not already in cache
                Dbg.Assert(cache[setIndex, i] != block_addr);
                if (cache[setIndex, i] == Proc.Proc.NULL_ADDRESS)
                {
                    emptyEntryIndex = i;
                    break;
                }
            }

            // Replace LRU
            if (emptyEntryIndex == -1)
            {
                lruEntryIndex = LruChainList[setIndex].Last.Value;
                UpdateLRU(setIndex, lruEntryIndex);
            }
            else
                LruChainList[setIndex].AddFirst(emptyEntryIndex);

            int replaceAssocIdx = (emptyEntryIndex != -1) ?
                emptyEntryIndex : lruEntryIndex;

            // Add the new block
            cache[setIndex, replaceAssocIdx] = block_addr;

            // **** Update benefits ****
            update_benefit(cmd, setIndex, replaceAssocIdx);
        }

        private void update_benefit(Cmd cmd, int setIndex, int replaceAssocIdx)
        {
            switch (cmd.Type)
            {
                case CmdType.ACT:
                    rb_BBC[setIndex, replaceAssocIdx].Benefit++;
                    //additional benefit due to tRAS
                    if (rb_BBC[setIndex, replaceAssocIdx].ColumnStreak < _trasColumnLen && rb_BBC[setIndex, replaceAssocIdx].ColumnStreak != 0)
                        rb_BBC[setIndex, replaceAssocIdx].Benefit++;
                    rb_BBC[setIndex, replaceAssocIdx].ColumnStreak = 0; // reset
                    break;

                case CmdType.PRE_BANK:
                    rb_BBC[setIndex, replaceAssocIdx].Benefit++;
                    break;

                case CmdType.RD:
                case CmdType.WR:
                case CmdType.WR_AP:
                case CmdType.RD_AP:
                    rb_BBC[setIndex, replaceAssocIdx].ColumnStreak++;
                    break;
            }
        }

        public void udpate_cache(ulong blockAddr, Cmd cmd)
        {
            //calculate set index
            int setIndex = (int)(blockAddr % SetMax);

            //search for block
            for (int i = 0; i < Assoc; i++)
            {
                if (cache[setIndex, i] == blockAddr)
                {
                    UpdateLRU(setIndex, i);
                    update_benefit(cmd, setIndex, i);
                }
            }
        }

        public void adjust_thresh(int num)
        {
            RbCacheThreshold += num;
        }

        public bool to_cache(ulong blockAddr)
        {
            //calculate set index
            int setIndex = (int)(blockAddr % SetMax);

            //search for block
            for (int i = 0; i < Assoc; i++)
            {
                if (cache[setIndex, i] == blockAddr)
                {
                    Hit++;
                    if (rb_BBC[setIndex, i].Benefit >= RbCacheThreshold)
                        return true;
                    return false;
                }
            }

            // Couldn't find block_addr; miss
            Miss++;
            return false;
        }
    }
}