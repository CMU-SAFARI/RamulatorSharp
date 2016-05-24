using Ramulator.MemCtrl.Refresh;
using Ramulator.Sim;
using System;

namespace Ramulator.MemCtrl
{
    public enum SALP
    {
        NONE,
        SALP1,
        SALP2,
        MASA,
    }

    public enum COPY
    {
        NAIVE_COPY,
        MEMCPY,

        // RowClone (Seshadri et al., MICRO 2013)
        RC_INTRA_SA,

        RC_INTER_SA,
        RC_INTER_BANK,

        // LISA (Chang et al., HPCA 2016)
        LISA_CLONE,

        // Probable copy switching b/w RC-intra and RC-inter SA
        RC_PROB_SA,

        // probable copy switching b/w RC-intra and LISA-inter SA
        RC_LISA_PROB_SA,
    }

    public enum VILLA_HOT
    {
        PRE,
        ACT,
        EPOCH,
        RBLA,
    }

    public class MemCtrlConfig : ConfigGroup
    {
        /* Page policy */
        public bool open_row_policy = false;

        /* SALP */
        public SALP salp = SALP.NONE;

        /* NoC latency */
        public uint xbar_latency = 16;

        /* queue size */
        public int readq_max_per_chan = 64;
        public int writeq_max_per_chan = 64;

        /* Write back mode */
        public string wbmode_algo = "DecoupledWBFullServeN";
        public Type typeof_wbmode_algo;
        public uint serve_max = 32;

        /* Track bank-level parallelism */
        public bool blp_tracking = false;

        /* Mapping method used to map a virtual addr to a physical addr */
        public bool page_randomize = true;
        public bool page_sequence = false;
        public bool page_contiguous = false;

        /* Refresh */
        public RefreshEnum refresh_policy = RefreshEnum.NONE;
        public ulong refresh_frequency = 2; // Issue refresh more frequently as in high temp.
        public bool b_refresh_bank = false; // Default: all-bank/rank-level refresh. Knob: turns on per-bank refresh.
        public bool round_robin_ref = false; // Instead of looking for pending refpb in bank0 first, do a round-robin check.
        public int max_delay_ref_counts = 8;

        // Special knob to allow per-bank refs to occur in parallel. Factor represents % of refresh to be overlapped.
        public double refpb_overlap_factor = 0;

        /* MASA */
        public bool b_piggyback_SEL_SA = true;
        public bool no_selsa_preemption = false;

        /*
         * Fast in-memory copy mechanisms
         */
        public COPY copy_method = COPY.MEMCPY; // Default memcpy (not in-memory): a bunch of mem requests
        public int copy_gran = 128; // # of cachelines (128 = 8KB)
        public int copyq_max = 64; // maximum number of copy's in flight

        // LISA
        public int lisa_rbm_latency = 8; // ns

        // Probable copy mechanism that has a certain probability copying data within a subarray
        public int rc_prob_intra_sa = 0; // 0% -> 100%

        // Naive block copy mechanism -- a copy command that activates both source and destination,
        // then transfer data one cache line at a time
        public int naive_block_copy_latency = -1; // cycles

        /*
         * VILLA -- Variable Latency DRAM (Chang et al., HPCA 2016)
         */
        public bool villa_cache = false;
        public double villa_tRCD_frac = 0.548; // relative to the baseline tRCD
        public double villa_tRAS_frac = 0.369; // relative to the baseline tRAS
        public double villa_tRP_frac = 0.618; // relative to the baseline tRP
        public uint villa_fast_sa_num_rows = 64;
        public VILLA_HOT villa_cache_method = VILLA_HOT.EPOCH;
        public bool villa_lru = false;
        public uint num_villa_sa = 4;
        public bool villa_ideal = false;

        /*
         * ChargeCache (Hassan et al., HPCA 2016)
         */
        public bool charge_cache = false;
        public uint cc_capacity = 512; // as number of rows per channel and per core
        public uint cc_access_latency = 1; // Highly-Charged Row Address Cache (HCRAC) access latency
        public uint cc_associativity = 16; // HCRAC associativity
        public double cc_caching_duration = 1.0; // HCRAC caching duration (as milliseconds)
        public double cc_tRCD_frac = 0.65; // relative to the baseline tRCD
        public double cc_tRAS_frac = 0.75; // relative to the baseline tRAS

        /* RBLA stats */
        public int rbla_cache_threshold = 150;
        public int rbla_epoch_clean_threshold = 1000000;
        public uint rbla_sets = 32;
        public uint rbla_assoc = 8;
        public int rbla_adjust_step = 10;

        /* Row hit hotness monitor */
        public int num_hit_counters = 4096; // per channel
        public int keep_hist_counters_per_sa = 4; // At the end of epoch only keep a subset of counters' values
        public int lru_assoc_per_subarray = 8;
        public double history_weight = 0.25;
        public ulong os_page_size = 4 * 1024;
        public long cache_mon_epoch = 10000;
        public CacheMonType cache_mon_type = CacheMonType.PerBank;
        public bool hit_track_half_row = false; // Track hotness at half row granularity
        public int memcache_hot_hit_thresh = 10;

        protected override bool set_special_param(string param, string val)
        {
            return false;
        }

        public override void finalize()
        {
            // wbmode algo
            string typeName = typeof(MemWBMode.MemWBMode).Namespace + "." + Config.mctrl.wbmode_algo;
            try
            {
                typeof_wbmode_algo = Type.GetType(typeName);
            }
            catch
            {
                throw new Exception(String.Format("WBMode not found {0}", Config.mctrl.wbmode_algo));
            }
        }
    }
}
