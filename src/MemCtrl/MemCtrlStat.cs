using Ramulator.Sim;

namespace Ramulator.MemCtrl
{
    public class MemCtrlStat : StatGroup
    {
        public uint cid;

        // load
        public AccumRateStat[] rbinaryloadtick_per_proc, rloadtick_per_proc, wbinaryloadtick_per_proc, wloadtick_per_proc;

        // writeback
        public AccumRateStat wbmode_fraction;

        public SampleAvgStat rds_per_wb_mode, wbs_per_wb_mode, wbmode_blp, wbmode_length, wbmode_distance;

        // Refresh
        public SampleAvgStat read_queue_latency_perchan;

        public AccumStat stall_on_refresh;

        // ChargeCache
        public AccumStat cc_hit, cc_miss;


        public MemCtrlStat(uint cid)
        {
            this.cid = cid;
            Init();
        }
    }
}
