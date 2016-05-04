using Ramulator.MemReq;
using System.Collections.Generic;
using System.Diagnostics;
using Ramulator.Sim;

namespace Ramulator.Proc
{
    public class CacheHierarchy
    {
        public Cache[] L1List;
        public Cache[] L2List;

        public CacheHierarchy(int numCores)
        {
            L1List = new Cache[numCores];
            L2List = new Cache[numCores];
            uint blockSize = (uint)Config.proc.block_size;

            Cache l2c = null;

            for (int i = 0; i < numCores; i++)
            {
                // Empty L1 cache if we only modeling one shared LLC!
                if (Config.proc.llc_shared_cache_only)
                {
                    L1List[i] = new Cache();
                    Dbg.AssertPrint(Config.proc.shared_l2, "Shared LLC option enabled! So shared_l2 knob needs to be true!");
                }
                else
                {
                    L1List[i] = new Cache(Config.proc.l1_cache_size,
                            Config.proc.l1_cache_assoc, blockSize,
                            Config.proc.l1_cache_hit_latency, i);
                }

                // One slice of private l2 for each core
                if (!Config.proc.shared_l2)
                {
                    L2List[i] = new Cache(Config.proc.l2_cache_size,
                            Config.proc.l2_cache_assoc, blockSize,
                            Config.proc.l2_cache_hit_latency, i, true);
                }
                else
                {
                    if (l2c == null)
                        l2c = new Cache(Config.proc.l2_cache_size,
                            Config.proc.l2_cache_assoc, blockSize,
                            Config.proc.l2_cache_hit_latency, -1, true);
                    L2List[i] = l2c;
                }
            }
        }
    }

    public class Cache
    {
        public uint SetMax;   //total number of sets
        public ulong Hit;      //number of cache hits
        public ulong Miss;     //number of cache misses

        // Cache configs
        public uint Assoc, Size, BlockSize, HitLatency;

        // MetaData
        public ulong[,] cache;     //tag for individual blocks [set_index, associativity]

        public bool[,] Dirty;      //dirty bit for individual blocks [set_index, associativity]
        public int[,] CoreId;

        // LRU chain based on assoc idx - the head is the MRU and the tail is LRU
        public LinkedList<int>[] LruChainList;

        // This queue is used to model latency
        public LinkedList<Req>[] HitQueuePerCore;

        // Processor ID: -1 means it's shared among all cores
        public int CachePid;

        public bool IsL2C;

        public bool IsVoid; // This cache is empty and does not really exist

        /**
         * Constructor
         */

        public Cache(uint cacheSize, uint cacheAssoc, uint cacheBlockSize, uint hitLatency, int pid = -1, bool isL2C = false)
        {
            Hit = Miss = 0;
            IsVoid = false;

            // Configs
            Dbg.Assert(cacheSize > 0);
            Size = cacheSize;
            Dbg.Assert(cacheAssoc > 0);
            Assoc = cacheAssoc;
            Dbg.Assert(cacheBlockSize > 0);
            BlockSize = cacheBlockSize;
            CachePid = pid;
            if (pid == -1)
                Dbg.Assert(Config.proc.shared_l2);
            HitLatency = hitLatency;
            IsL2C = isL2C;

            //size of a set in bytes
            uint setSize = BlockSize * Assoc;

            //total number of sets
            Debug.Assert((cacheSize % setSize) == 0);
            SetMax = Size / setSize;

            // components
            cache = new ulong[SetMax, Assoc];
            Dirty = new bool[SetMax, Assoc];
            CoreId = new int[SetMax, Assoc];
            LruChainList = new LinkedList<int>[SetMax];

            // initialize tags
            for (int i = 0; i < SetMax; i++)
            {
                for (int j = 0; j < Assoc; j++)
                {
                    cache[i, j] = Proc.NULL_ADDRESS;
                    CoreId[i, j] = -1;
                }
                LruChainList[i] = new LinkedList<int>();
            }

            // Hit queue
            // Shared cache -- one hit queue for each core
            // Private cache -- one hit queue only
            int numCores = (pid == -1) ? Config.N : 1;
            HitQueuePerCore = new LinkedList<Req>[numCores];
            for (int i = 0; i < numCores; i++)
                HitQueuePerCore[i] = new LinkedList<Req>();
        }

        // Create an empty cache hierarchy! Used for one level of shared LLC.
        public Cache()
        {
            IsVoid = true;
            HitQueuePerCore = new LinkedList<Req>[1];
            HitQueuePerCore[0] = new LinkedList<Req>();
        }

        public LinkedList<Req> get_hit_queue(int pid)
        {
            if (IsVoid)
                return HitQueuePerCore[0];
            return CachePid == -1 ? HitQueuePerCore[pid] : HitQueuePerCore[0];
        }

        public void UpdateLRU(int set, int assoc)
        {
            if (IsVoid)
                return;
            Dbg.Assert(LruChainList[set].Count <= Assoc);
            Dbg.Assert(LruChainList[set].Remove(assoc));
            LruChainList[set].AddFirst(assoc);
        }

        /**
         * Searches for a block within the cache.
         * Also updates the state of the cache for a dirty block hit.
         * If found and the access is a write type, sets the dirty bit.
         *
         * @param block_addr block address
         * @param inst_type instruction type
         * @return if found, true; otherwise, false
         */

        public virtual bool is_cache_hit(ulong block_addr, ReqType inst_type)
        {
            if (IsVoid)
                return false;

            //calculate set index
            int setIndex = (int)(block_addr % SetMax);

            //search for block
            for (int i = 0; i < Assoc; i++)
            {
                if (cache[setIndex, i] == block_addr)
                {
                    Hit++;
                    UpdateLRU(setIndex, i);
                    if (inst_type == ReqType.WRITE)
                    {
                        Dirty[setIndex, i] = true;
                    }
                    CacheStats(inst_type, true, block_addr);
                    return true;
                }
            }

            // Couldn't find block_addr; miss
            Miss++;
            CacheStats(inst_type, false, block_addr);
            return false;
        }

        public virtual void CacheStats(ReqType req_type, bool hit, ulong block_addr = 0)
        {
            if (IsVoid)
                return;

            int l2Idx = (CachePid == -1) ? 0 : CachePid;
            // Cache miss
            if (!hit)
            {
                // L1
                if (!IsL2C)
                {
                    Stat.procs[CachePid].l1c_read_hit_rate.collect(0);
                    Stat.procs[CachePid].l1c_write_hit_rate.collect(0);
                    Stat.procs[CachePid].l1c_total_hit_rate.collect(0);
                    if (req_type == ReqType.WRITE)
                        Stat.procs[CachePid].l1c_write_misses.collect();
                    else if (req_type == ReqType.READ)
                        Stat.procs[CachePid].l1c_read_misses.collect();
                }
                else
                {
                    Stat.caches[l2Idx].l2c_read_hit_rate.collect(0);
                    Stat.caches[l2Idx].l2c_write_hit_rate.collect(0);
                    Stat.caches[l2Idx].l2c_total_hit_rate.collect(0);
                    if (req_type == ReqType.WRITE)
                        Stat.caches[l2Idx].l2c_write_misses.collect();
                    else if (req_type == ReqType.READ)
                        Stat.caches[l2Idx].l2c_read_misses.collect();
                }
                return;
            }

            // Cache hits
            if (req_type == ReqType.WRITE)
            {
                // L1
                if (!IsL2C)
                {
                    Stat.procs[CachePid].l1c_write_hits.collect();
                    Stat.procs[CachePid].l1c_write_hit_rate.collect(1);
                    Stat.procs[CachePid].l1c_total_hit_rate.collect(1);
                }
                // L2
                else
                {
                    Stat.caches[l2Idx].l2c_write_hits.collect();
                    Stat.caches[l2Idx].l2c_write_hit_rate.collect(1);
                    Stat.caches[l2Idx].l2c_total_hit_rate.collect(1);
                }
            }
            else
            {
                // Stats
                if (!IsL2C)
                {
                    Stat.procs[CachePid].l1c_read_hits.collect();
                    Stat.procs[CachePid].l1c_read_hit_rate.collect(1);
                    Stat.procs[CachePid].l1c_total_hit_rate.collect(1);
                }
                else
                {
                    Stat.caches[l2Idx].l2c_read_hits.collect();
                    Stat.caches[l2Idx].l2c_read_hit_rate.collect(1);
                    Stat.caches[l2Idx].l2c_total_hit_rate.collect(1);
                }
            }
        }

        // Simply check if the cacheline is still in the cache without changing the state of the cache
        public bool in_cache(ulong blockAddr)
        {
            if (IsVoid)
                return false;

            //calculate set index
            uint setIndex = (uint)(blockAddr % SetMax);
            //search for block
            for (int i = 0; i < Assoc; i++)
                if (cache[setIndex, i] == blockAddr)
                    return true;
            return false;
        }

        public bool cache_remove(ulong blockAddr, ReqType instType)
        {
            if (IsVoid)
                return false;

            //calculate set index
            uint setIndex = (uint)(blockAddr % SetMax);

            //search for block
            for (int i = 0; i < Assoc; i++)
            {
                if (cache[setIndex, i] == blockAddr)
                {
                    cache[setIndex, i] = Proc.NULL_ADDRESS;
                    Dbg.Assert(LruChainList[setIndex].Remove(i));
                    return true;
                }
            }

            //couldn't find block_addr; miss
            Miss++;
            return false;
        }

        /**
         * Add block to the cache.
         * Either an empty or the LRU block is populated.
         * @param block_addr block address
         * @param inst_type instruction type
         * @return if LRU block is populated, its tag; if empty block is populated, 0
         */

        public virtual ulong cache_add(ulong block_addr, ReqType inst_type, int pid)
        {
            if (IsVoid)
                return Proc.NULL_ADDRESS;

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
                if (cache[setIndex, i] == Proc.NULL_ADDRESS)
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

            ulong return_addr = Proc.NULL_ADDRESS;
            int replaceAssocIdx = (emptyEntryIndex != -1) ?
                emptyEntryIndex : lruEntryIndex;

            // Check if the replacement is non-empty and dirty
            if ((replaceAssocIdx == lruEntryIndex) && Dirty[setIndex, lruEntryIndex])
            {
                return_addr = cache[setIndex, lruEntryIndex];
                if (!IsL2C)
                    Stat.procs[CachePid].l1c_dirty_eviction.collect();
                else
                    Stat.caches[CachePid == -1 ? 0 : CachePid].l2c_dirty_eviction.collect();
            }

            // Add the new block
            cache[setIndex, replaceAssocIdx] = block_addr;
            Dirty[setIndex, replaceAssocIdx] = (inst_type == ReqType.WRITE);
            CoreId[setIndex, replaceAssocIdx] = pid; // for partitioning
            return return_addr;
        }
    }//class
}//namespace