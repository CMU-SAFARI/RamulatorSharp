using Ramulator.Sim;

namespace Ramulator
{
    public class ProcConfig : ConfigGroup
    {
        // Issue width of each out-of-order core
        public int ipc = 3;

        // Writebacks
        public bool wb = true;

        // Instruction window size
        public int inst_wnd_max = 128;

        // Number of MSHRs
        public int mshr_max = 32;

        // Writeback queue
        public int wb_q_max = 32;

        public int read_write_q_max = 32;

        public bool issue_on_dup_req = true;

        ////////////////////////////////////
        // Knobs for configuring caches
        ////////////////////////////////////

        // Cache block size in power of 2
        public int block_size_bits = 6;

        // Cache block size in bytes
        public int block_size;

        // Turn on cache?
        public bool cache_enabled = false;

        // If the cache is one, is it a shared LLC?
        public bool llc_shared_cache_only = false;

        // L1 cache size in power of 2. Ex: 15->32KB
        public uint l1_cache_size = 15;

        // L1 cache associativity
        public uint l1_cache_assoc = 8;

        // L1 cache hit latency in cycles
        public uint l1_cache_hit_latency = 4;

        // Is L2 shared by all cores
        public bool shared_l2 = true;

        // L2 cache size in power of 2. Ex: 22->4MB
        public uint l2_cache_size = 22;

        // L2 cache associativity
        public uint l2_cache_assoc = 8;

        // L2 cache hit latency in cycles
        public uint l2_cache_hit_latency = 20;

        // Perfect memory that returns within the same cycle
        public bool ideal_memory = false;

        // Traces with cloning and setting calls
        public bool b_read_rc_traces = false;

        public bool stats_exclude_cpy = false;

        protected override bool set_special_param(string param, string val)
        {
            return false;
        }

        public override void finalize()
        {
            block_size = 1 << block_size_bits;
            l2_cache_size = (uint)(1 << (int)l2_cache_size);
            l1_cache_size = (uint)(1 << (int)l1_cache_size);
        }
    }
}
