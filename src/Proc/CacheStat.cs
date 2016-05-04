using Ramulator.Sim;

namespace Ramulator.Proc
{
    /**
     * LLC statistics
     */

    public class CacheStat : StatGroup
    {
        public SampleAvgStat l2c_read_hit_rate;
        public SampleAvgStat l2c_write_hit_rate;
        public SampleAvgStat l2c_total_hit_rate;
        public AccumStat l2c_read_hits;
        public AccumStat l2c_read_misses;
        public AccumStat l2c_write_hits;
        public AccumStat l2c_write_misses;
        public AccumStat l2c_dirty_eviction;
        public DictSampleStat rd_req_word_offset;

        // This is used to calculate Energy/Inst when we run the simulation in instruction mode so that we can continue
        // collecting retired inst for those apps that finsih first.
        public AccumStat total_system_inst_executed;

        public CacheStat()
        {
            Init();
        }
    }
}