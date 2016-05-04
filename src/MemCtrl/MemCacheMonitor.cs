using System.Collections.Generic;
using System.Linq;
using Ramulator.MemReq;
using Ramulator.Sim;

namespace Ramulator.MemCtrl
{
    /*
     * Goal: find hot OS pages or DRAM rows in order to cache them.
     */

    public enum CacheMonType
    {
        PerCore,
        PerChan,
        PerBank
    }

    public class MemCacheMonitor
    {
        // Each core has a list of hit counters. Each address has its own counter.
        public Dictionary<ulong, int>[] PerCoreAddrHitCounters;

        // Each bank has a list of hit counters. Each address has its own counter.
        public Dictionary<ulong, int>[,] PerBankAddrHitCounters;

        // Shared hit counters for all cores
        public Dictionary<ulong, int> AddrHitCounters;

        // LRU list per bank
        public LinkedList<ulong>[,] PerBankAddrLru;

        // Maximum number of counters, which means the maximum number of addresses to keep track of.
        private readonly int _numHitCountersPerCore;

        private readonly int _numHitCountersPerBank;

        // Maximum number of counters, which means the maximum number of addresses to keep track of.
        private readonly int _numHitCounters;

        private readonly ulong _osPageSize;

        // Ref to the memory controller
        private readonly MemCtrl _mctrl;

        // Granularity of monitoring
        private readonly CacheMonType _cMonType;

        // A list of hot addresses to cache -- the address is either an os page or DRAM row.
        private readonly HashSet<ulong> _hotAddresses;

        // Number of hot address hit counters to keep per channel -- calculated based on number of subarrays
        private readonly int _numHotHitCountersPerChan;

        // Number of elapsed epochs
        private int _numEpochs;

        public MemCacheMonitor(MemCtrl mctrl)
        {
            // Per core - page granularity
            PerCoreAddrHitCounters = new Dictionary<ulong, int>[Config.N];
            for (int i = 0; i < Config.N; i++)
                PerCoreAddrHitCounters[i] = new Dictionary<ulong, int>();
            // Per bank - row granularity
            PerBankAddrHitCounters = new Dictionary<ulong, int>[mctrl.Rmax, mctrl.Bmax];
            PerBankAddrLru = new LinkedList<ulong>[mctrl.Rmax, mctrl.Bmax];
            for (int r = 0; r < mctrl.Rmax; r++)
                for (int b = 0; b < mctrl.Bmax; b++)
                {
                    PerBankAddrHitCounters[r, b] = new Dictionary<ulong, int>();
                    PerBankAddrLru[r, b] = new LinkedList<ulong>();
                }
            // Per channel - page granularity
            AddrHitCounters = new Dictionary<ulong, int>();

            _numHitCounters = Config.mctrl.num_hit_counters;
            _numHitCountersPerCore = _numHitCounters / Config.N;
            _numHitCountersPerBank = _numHitCounters / (int)mctrl.Rmax / (int)mctrl.Bmax;

            _osPageSize = Config.mctrl.os_page_size;
            _mctrl = mctrl;
            _cMonType = Config.mctrl.cache_mon_type;
            _hotAddresses = new HashSet<ulong>();
            _numHotHitCountersPerChan = (int)(mctrl.Rmax * mctrl.Bmax * Config.mem.subarray_max * Config.mctrl.keep_hist_counters_per_sa);
            _numEpochs = 0;
        }

        // Record hit counts at OS page or DRAM row granularity
        public void record_addr_hit(Req req)
        {
            // OS page address = physical page number
            ulong pageAddr = req.Paddr / _osPageSize;
            // DRAM row address
            ulong rowAddr = req.Addr.rowid;
            Dictionary<ulong, int> dictToUpdate;

            // Update based on type
            switch (_cMonType)
            {
                case CacheMonType.PerCore:
                    dictToUpdate = PerCoreAddrHitCounters[req.Pid];
                    update_addr_in_dict(dictToUpdate, pageAddr, _numHitCountersPerCore);
                    break;

                case CacheMonType.PerChan:
                    update_addr_in_dict(AddrHitCounters, pageAddr, _numHitCounters);
                    break;

                case CacheMonType.PerBank:
                    if (Config.mctrl.hit_track_half_row)
                    {
                        // Encode if top or bottom half of a row by adding a bit to the LSB.
                        // 0 - top half ; 1 - bottom half
                        bool isTopHalfRow = (req.Addr.colid < (_mctrl.ddr3.COL_MAX / 2));
                        rowAddr <<= 1;
                        if (!isTopHalfRow)
                            rowAddr |= 0x01;
                    }
                    dictToUpdate = PerBankAddrHitCounters[req.Addr.rid, req.Addr.bid];
                    update_addr_in_dict(dictToUpdate, rowAddr, _numHitCountersPerBank);
                    break;

                default:
                    throw new System.Exception("Unspecified cache monitor type.");
            }
        }

        // Update or add the address to monitor hit count. If the number exceeds the capacity, drops it.
        private void update_addr_in_dict(Dictionary<ulong, int> dict, ulong key, int capacity)
        {
            if (dict.ContainsKey(key))
                dict[key]++;
            else if (dict.Count >= capacity)
                return;
            else
                dict.Add(key, 1);
        }

        // Get a list of potential hot addresses to cache at the end of an epoch
        public void end_epoch()
        {
            int numCountersToKeep = _numHotHitCountersPerChan;
            double historyWeight = Config.mctrl.history_weight;
            _hotAddresses.Clear();

            switch (_cMonType)
            {
                case CacheMonType.PerCore:
                    for (int i = 0; i < Config.N; i++)
                        sort_update_dict(PerCoreAddrHitCounters[i],
                            numCountersToKeep / Config.N, historyWeight);
                    break;

                case CacheMonType.PerChan:
                    sort_update_dict(AddrHitCounters, numCountersToKeep, historyWeight);
                    break;

                case CacheMonType.PerBank:
                    for (int r = 0; r < _mctrl.Rmax; r++)
                        for (int b = 0; b < _mctrl.Bmax; b++)
                            sort_update_dict(PerBankAddrHitCounters[r, b],
                                numCountersToKeep / (int)_mctrl.Rmax / (int)_mctrl.Bmax, historyWeight);
                    break;

                default:
                    throw new System.Exception("Unspecified cache monitor type.");
            }

            _numEpochs++;
            Dbg.AssertPrint(_hotAddresses.Count <= numCountersToKeep, "Too many hot addresses");
        }

        // 1. Sort the dictionary based on the hit counts
        // 2. Select top X number of entries as specified and put them into the "hot" hash table
        public void sort_update_dict(Dictionary<ulong, int> dict, int numCountersToKeep, double historyWeight)
        {
            List<KeyValuePair<ulong, int>> sortedDict = dict.ToList();
            // Descending order
            sortedDict.Sort((firstPair, nextPair) => nextPair.Value.CompareTo(firstPair.Value));

            // Clear counters
            dict.Clear();

            // Insert into the hot hash table and add back to the hash table
            int count = 0;
            foreach (KeyValuePair<ulong, int> item in sortedDict)
            {
                if (count == numCountersToKeep)
                    break;
                if (item.Value < Config.mctrl.memcache_hot_hit_thresh)
                    continue;
                _hotAddresses.Add(item.Key);
                dict.Add(item.Key, (int)(item.Value * historyWeight));
                count++;
            }
        }

        // See if the target address is hot or not
        public bool is_req_hot(Req req)
        {
            // During the first epoch with zero history, always return true.
            if (_numEpochs == 0)
                return false;

            // OS page address = physical page number
            ulong pageAddr = req.Paddr / _osPageSize;
            // DRAM row address
            ulong rowAddr = req.Addr.rowid;

            switch (_cMonType)
            {
                case CacheMonType.PerCore:
                case CacheMonType.PerChan:
                    return _hotAddresses.Contains(pageAddr);

                case CacheMonType.PerBank:
                    if (Config.mctrl.hit_track_half_row)
                    {
                        // Encode if top or bottom half of a row by adding a bit to the LSB.
                        // 0 - top half ; 1 - bottom half
                        bool isTopHalfRow = (req.Addr.colid < (_mctrl.ddr3.COL_MAX / 2));
                        rowAddr <<= 1;
                        if (!isTopHalfRow)
                            rowAddr |= 0x01;
                    }
                    return _hotAddresses.Contains(rowAddr);

                default:
                    throw new System.Exception("Unspecified cache monitor type.");
            }
        }

        // Determine if a physical row buffer (half a row) is hot or not
        public bool is_row_hot(ulong rowAddr, bool topHalf)
        {
            // During the first epoch with zero history, always return true.
            if (_numEpochs == 0)
                return true;
            rowAddr <<= 1;
            if (!topHalf)
                rowAddr |= 0x01;
            return _hotAddresses.Contains(rowAddr);
        }

        public bool is_first_epoch()
        {
            return _numEpochs == 0;
        }

        // Clear all the records
        public void clear_counters()
        {
            for (int i = 0; i < Config.N; i++)
                PerCoreAddrHitCounters[i].Clear();
            AddrHitCounters.Clear();
            for (int r = 0; r < _mctrl.Rmax; r++)
                for (int b = 0; b < _mctrl.Bmax; b++)
                    PerBankAddrHitCounters[r, b].Clear();
        }
    }
}