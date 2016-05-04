using Ramulator.Sim;

namespace Ramulator.Mem
{
    public class MemConfig : ConfigGroup
    {
        /* ddr3 */
        public DDR3DRAM.DDR3Enum ddr3_type = DDR3DRAM.DDR3Enum.DDR3_4Gb_x8_1333_10_10_10;

        public int tRTRS = -1;
        public uint tWR = 0;
        public uint tWTR = 0;
        public uint tBL = 0;
        public int tRP = -1;
        public int tRCD = -1;
        public int tRAS = -1;

        public uint subarray_max = 0;
        public uint col_max = 0;

        public uint tRA = 0;
        public uint tWA = 0;

        // Refresh
        public int tREFI = -1;

        public int tRFC = -1;

        // Power integrity - FAW which affects RRD (=FAW/5)
        public uint tFAW = 0;

        public double rank_to_bank_trp_ratio = 1.16; // extrapolated from LPDDR2&3
        public double rank_to_bank_trfc_ratio = 2.16; // extrapolated from LPDDR2&3

        /* mapping */
        public MemMap.MapEnum map_type = MemMap.MapEnum.ROW_RANK_BANK_COL_CHAN;
        public uint col_per_subrow;
        public bool mod_sa_assign = false; // Assign subarray id by doing (PROC_ID)%(NUM_SA)

        /* scale time */
        public uint clock_factor = 6;

        /* physical configuration */
        public uint chan_max = 1;
        public uint rank_max = 1;
        public uint bank_max = 8;

        // Study on the effect of having multiple row buffers -- Works with those mechanisms that operate on subarrays
        public int max_row_buffer_count = 1;

        // Copy hop count for LISA
        public uint lisa_inter_sa_hop_count = 1;

        protected override bool set_special_param(string param, string val)
        {
            return false;
        }

        public override void finalize()
        {
        }
    }
}