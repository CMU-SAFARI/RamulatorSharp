using Ramulator.Sim;

namespace Ramulator
{
    // Processor statistics
    public class ProcStat : StatGroup
    {
        //trace
        public string trace_fname;

        //instructions
        public AccumStat cycle;

        public AccumRateStat ipc;  //total instructions, mem and non-mem, retired (executed) by instruction window

        // Memory inst
        public AccumRateStat rmpc, wmpc, cmpc;

        public AccumStat read_misses, write_misses, copy_misses;

        // Cache
        public SampleAvgStat l1c_read_hit_rate, l1c_write_hit_rate, l1c_total_hit_rate;

        public AccumStat l1c_read_hits, l1c_read_misses, l1c_write_hits, l1c_write_misses, l1c_dirty_eviction;

        //stall
        public AccumStat stall_inst_wnd, stall_read_mctrl, stall_write_mctrl, stall_copy_mctrl, stall_mshr;

        //memory request issued (sent to memory scheduler)
        public AccumStat req;          //total memory requests issued

        public AccumStat read_req;     //read (load) requests issued
        public AccumStat write_req;    //write (store) requests issued
        public AccumStat copy_req;     //copy requests issued
        public AccumStat allocated_physical_pages;

        // Memory request served
        public AccumStat read_req_served, write_req_served, copy_req_served;

        // Per-quantum stats
        public PerQuantumStat insts_per_quantum, reads_per_megainst, writes_per_megainst;

        // Writeback hit
        public AccumStat wb_hit;

        // Row-buffer related stats
        public AccumStat row_hit_read, row_miss_read, row_hit_write, row_miss_write;

        public SamplePercentAvgStat row_hit_rate_read, row_hit_rate_write;

        // Latency (time between when a request is issued and served)
        public SampleAvgStat read_avg_latency, write_avg_latency, l1_cache_hit_avg_latency,
            l2_cache_hit_avg_latency, copy_avg_latency;

        // Bank-level parallelism
        public SampleAvgStat service_blp;

        // Queueing latency
        public SampleAvgStat read_queue_latency_perproc;

        public PerQuantumStat read_queue_latency_per_quantum; //every 100K cycles

        // Copy range
        public AccumStat chan_copy, rank_copy, bank_copy, inter_sa_copy, intra_sa_copy;

        public ProcStat()
        {
            Init();
        }
    }
}