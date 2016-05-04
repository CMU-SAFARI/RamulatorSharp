using Ramulator.Sim;

namespace Ramulator
{
    public class BankStat : StatGroup
    {
        // channel/rank/bank id
        public uint cid, rid, bid;

        // service
        public AccumRateStat utilization;

        // commands
        public AccumStat cmd_act, cmd_sel_sa, cmd_pre_rank, cmd_pre_bank, cmd_pre_sa,
            cmd_ref_rank, cmd_ref_bank,
            cmd_rd, cmd_wr, cmd_rd_ap, cmd_wr_ap,
            cmd_rc_intra_sa, cmd_rc_inter_sa, cmd_rc_inter_bank, cmd_links_inter_sa, cmd_base_inter_sa;

        // memory accesses
        public AccumStat access;

        // hit or miss
        public AccumStat row_hit, row_miss;

        // hit or miss per originating processor
        public AccumStat[] row_hit_perproc, row_miss_perproc;

        // Opened subarrays
        public SampleAvgStat avg_open_subarrays;

        /* VILLA Cache */
        public SampleAvgStat villa_hit_rate;
        public AccumStat villa_misses, villa_hits;

        public BankStat(uint cid, uint rid, uint bid)
        {
            this.cid = cid;
            this.rid = rid;
            this.bid = bid;
            Init();
        }
    }
}