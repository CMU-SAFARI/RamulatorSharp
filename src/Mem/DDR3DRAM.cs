using Ramulator.MemCtrl;
using Ramulator.Sim;
using System;

namespace Ramulator.Mem
{
    public class DDR3DRAM
    {
        public enum DDR3Enum
        {
            //tRCD-tRP-tCL
            DDR3_4Gb_x8_1066_8_8_8,

            DDR3_4Gb_x8_1333_10_10_10,
            DDR3_4Gb_x8_1600_11_11_11,
            DDR3_4Gb_x8_1866_13_13_13,
            DDR3_8Gb_x8_1066_7_7_7,
            DDR3_8Gb_x8_1333_9_9_9,
            DDR3_8Gb_x8_1600_11_11_11,
            LPDDR3_8Gb_x8_1333,
        }

        public uint CHANNEL_WIDTH = 64;
        public uint BANK_MAX = 8;
        public uint ROW_MAX;
        public uint COL_MAX;
        public uint DEVICE_WIDTH;
        public uint DEVICES_PER_RANK;
        public uint SUBARRAYS_PER_BANK;

        public Timing timing;

        //constructor
        public DDR3DRAM(DDR3Enum type, uint clock_factor, int tRTRS, uint tWR, uint tWTR, uint tBL, uint bank_max,
            uint subarray_max, uint col_max, uint tRA, uint tWA, int tREFI, int tRFC, int tRP, int tRCD)
        {
            timing = new Timing();
            switch (type)
            {
                case DDR3Enum.DDR3_4Gb_x8_1066_8_8_8:
                    DDR3_4Gb_x8_1066_8_8_8();
                    break;

                case DDR3Enum.DDR3_4Gb_x8_1333_10_10_10:
                    DDR3_4Gb_x8_1333_10_10_10();
                    break;

                case DDR3Enum.DDR3_4Gb_x8_1600_11_11_11:
                    DDR3_4Gb_x8_1600_11_11_11();
                    break;

                case DDR3Enum.DDR3_8Gb_x8_1066_7_7_7:
                    DDR3_8Gb_x8_1066_7_7_7();
                    break;

                case DDR3Enum.DDR3_8Gb_x8_1333_9_9_9:
                    DDR3_8Gb_x8_1333_9_9_9();
                    break;

                case DDR3Enum.LPDDR3_8Gb_x8_1333:
                    LPDDR3_8Gb_x8_1333();
                    break;

                case DDR3Enum.DDR3_8Gb_x8_1600_11_11_11:
                    DDR3_8Gb_x8_1600_11_11_11();
                    break;

                case DDR3Enum.DDR3_4Gb_x8_1866_13_13_13:
                    DDR3_4Gb_x8_1866_13_13_13();
                    break;

                default:
                    throw new Exception("Invalid DRAM type.");
            }

            if (tRTRS != -1) timing.tRTRS = (uint)tRTRS;
            if (tWR != 0) timing.tWR = tWR;
            if (tWTR != 0) timing.tWTR = tWTR;
            if (tREFI != -1) timing.tREFI = (ulong)Math.Floor(tREFI / timing.tCK); // in ns
            if (tRFC != -1) timing.tRFC = (uint)Math.Ceiling(tRFC / timing.tCK); // in ns

            if (Config.mem.tRAS != -1)
            {
                timing.tRAS = (uint)Config.mem.tRAS;
                timing.tRC = timing.tRP + timing.tRAS;
            }

            if (tRP != -1)
            {
                timing.tRP = (uint)tRP;
                timing.tRC = timing.tRP + timing.tRAS;
            }

            if (tRCD != -1)
            {
                // Just tRCD without updating tRAS or tRC values
                timing.tRCD = (uint)tRCD;
            }

            // Configurable tFAWs and tRRDs
            if (Config.mem.tFAW != 0)
            {
                timing.tFAW = Config.mem.tFAW;
                timing.tRRD = Config.mem.tFAW / 5;
            }

            if (tBL != 0 && tBL > timing.tBL)
            {
                timing.tBL = tBL;

                /* COL-to-FAKT */
                timing.tRA = timing.tBL;
                timing.tWA = timing.tCWL + timing.tBL + (timing.tWR / 2);

                /* COL-to-PRE */
                timing.tRTP = timing.tBL;
                //tWTP is covered by (tCWL + tBL + tWR)

                /* COL-to-COL */
                timing.tCCD = timing.tBL;
                timing.tRTW = timing.tCL - timing.tCWL + timing.tBL + 2;
                //tWTR is covered by (tCWL + tBL + tWTR)
            }

            if (bank_max != 0) BANK_MAX = bank_max;
            if (subarray_max != 0)
                SUBARRAYS_PER_BANK = subarray_max;
            else
                Dbg.AssertPrint(Config.mctrl.salp == SALP.NONE, "The number of subarray needs to be > 0 in SALP mode.");

            if (col_max != 0) COL_MAX = col_max;

            if (tRA != 0) timing.tRA = tRA;
            if (tWA != 0) timing.tWA = tWA;

            // Scale the memory latency with respective to the processor's frequency
            timing.Scale(clock_factor, COL_MAX);
        }

        private void DDR3_8Gb_x8_1066_7_7_7()
        {
            BANK_MAX = 8;   // fixed for DDR3
            ROW_MAX = 128 * 1024; // assume 8 devices
            COL_MAX = 8 * 1024 / 64;
            DEVICE_WIDTH = 8;
            DEVICES_PER_RANK = CHANNEL_WIDTH / DEVICE_WIDTH;
            SUBARRAYS_PER_BANK = 8;

            timing.tCK = 1.875;    // ns

            timing.tRC = 27;   // 7_7_7
            timing.tRAS = 20;
            timing.tRP = 7;    // 7_7_7

            timing.tCCD = 4;
            timing.tWTR = 4;

            timing.tCL = 7;    // 7_7_7
            timing.tCWL = 6;
            timing.tBL = 4;

            timing.tRCD = 7;   // 7_7_7
            timing.tRTP = 4;
            timing.tWR = 8;

            timing.tRRD = 6;  // Page size 1KB
            timing.tFAW = 22; // Page size 1KB
            timing.tRFC = (uint)Math.Ceiling(350.0 / timing.tCK);
            timing.tREFI = (ulong)Math.Floor(64000000 / 8192 / timing.tCK);
            timing.tRTRS = 2;
            timing.tRTW = (timing.tCL - timing.tCWL + timing.tCCD + 2);

            // subarray
            timing.tRA = (uint)Math.Ceiling(timing.tCL / 2.0);
            timing.tWA = timing.tCWL + timing.tBL + (uint)Math.Ceiling(timing.tWR / 2.0);
            timing.tSCD = 1;
        }

        private void DDR3_8Gb_x8_1333_9_9_9()
        {
            BANK_MAX = 8;   // fixed for DDR3
            ROW_MAX = 128 * 1024;
            COL_MAX = 8 * 1024 / 64;
            DEVICE_WIDTH = 8;
            DEVICES_PER_RANK = CHANNEL_WIDTH / DEVICE_WIDTH;
            SUBARRAYS_PER_BANK = 8;

            timing.tCK = 1.5;    // ns

            timing.tRC = 33;
            timing.tRAS = 24;
            timing.tRP = 9;    // 9_9_9

            timing.tCCD = 4;
            timing.tWTR = 4;

            timing.tCL = 9;    // 9_9_9
            timing.tCWL = 7;
            timing.tBL = 4;

            timing.tRCD = 9;   // 9_9_9
            timing.tRTP = 4;
            timing.tWR = 10;

            timing.tRRD = 4;  // Page size 2KB
            timing.tFAW = 20; // Page size 2KB
            timing.tRFC = (uint)Math.Ceiling(350.0 / timing.tCK);
            timing.tREFI = (ulong)Math.Floor(64000000 / 8192 / timing.tCK);
            timing.tRTRS = 2;
            timing.tRTW = (timing.tCL - timing.tCWL + timing.tCCD + 2);

            // subarray
            timing.tRA = (uint)Math.Ceiling(timing.tCL / 2.0);
            timing.tWA = timing.tCWL + timing.tBL + (uint)Math.Ceiling(timing.tWR / 2.0);
            timing.tSCD = 1;
        }

        private void DDR3_8Gb_x8_1600_11_11_11()
        {
            BANK_MAX = 8;   // fixed for DDR3
            ROW_MAX = 128 * 1024;
            COL_MAX = 8 * 1024 / 64;
            DEVICE_WIDTH = 8;
            DEVICES_PER_RANK = CHANNEL_WIDTH / DEVICE_WIDTH;
            SUBARRAYS_PER_BANK = 8;

            timing.tCK = 1.25;    // ns

            // Unit: cycles
            timing.tRC = 39;   // 11_11_11
            timing.tRAS = 28;
            timing.tRP = 11;    // 11_11_11

            timing.tCCD = 4;
            timing.tWTR = 6;

            timing.tCL = 11;    // 11_11_11
            timing.tCWL = 8;
            timing.tBL = 4;

            timing.tRCD = 11;   // 11_11_11
            timing.tRTP = 6;
            timing.tWR = 12;

            timing.tRRD = 6;  // Page size 1KB
            timing.tFAW = 24; // Page size 1KB
            timing.tRFC = (uint)Math.Ceiling(350.0 / timing.tCK);
            timing.tREFI = (ulong)Math.Floor(64000000 / 8192 / timing.tCK);
            timing.tRTRS = 2;
            timing.tRTW = (timing.tCL - timing.tCWL + timing.tCCD + 2);

            // subarray
            timing.tRA = (uint)Math.Ceiling(timing.tCL / 2.0);
            timing.tWA = timing.tCWL + timing.tBL + (uint)Math.Ceiling(timing.tWR / 2.0);
            timing.tSCD = 1;
        }

        private void LPDDR3_8Gb_x8_1333() // Page size 2KB
        {
            BANK_MAX = 8;   // fixed for DDR3
            ROW_MAX = 128 * 1024;
            COL_MAX = 8 * 1024 / 64;
            DEVICE_WIDTH = 8;
            DEVICES_PER_RANK = CHANNEL_WIDTH / DEVICE_WIDTH;
            SUBARRAYS_PER_BANK = 8;

            timing.tCK = 1.5;    // ns

            timing.tRC = 42;
            timing.tRAS = 28;
            timing.tRP = 14;    // 9_9_9

            timing.tCCD = 4;
            timing.tWTR = 5;

            timing.tCL = 10;    // 9_9_9
            timing.tCWL = 6;
            timing.tBL = 4;

            timing.tRCD = 12;   // 9_9_9
            timing.tRTP = 5;
            timing.tWR = 10;

            timing.tRRD = 7;  // Page size 2KB
            timing.tFAW = 34; // Page size 2KB
            timing.tRFC = (uint)Math.Ceiling(210.0 / timing.tCK);
            timing.tREFI = (ulong)Math.Floor(32000000 / 8192 / timing.tCK);
            timing.tRTRS = 2;
            timing.tRTW = (timing.tCL - timing.tCWL + timing.tCCD + 2);

            // subarray
            timing.tRA = (uint)Math.Ceiling(timing.tCL / 2.0);
            timing.tWA = timing.tCWL + timing.tBL + (uint)Math.Ceiling(timing.tWR / 2.0);
            timing.tSCD = 1;
        }

        private void DDR3_4Gb_x8_1066_8_8_8()
        {
            BANK_MAX = 8;   // fixed for DDR3
            ROW_MAX = 64 * 1024;
            COL_MAX = 8 * 1024 / 64;
            DEVICE_WIDTH = 8;
            DEVICES_PER_RANK = CHANNEL_WIDTH / DEVICE_WIDTH;
            SUBARRAYS_PER_BANK = 8;

            timing.tCK = 1.875;    // ns

            timing.tRC = 28;
            timing.tRAS = 20;
            timing.tRP = 8;    // 8_8_8

            timing.tCCD = 4;
            timing.tWTR = 4;

            timing.tCL = 8;    // 8_8_8
            timing.tCWL = 6;
            timing.tBL = 4;

            timing.tRCD = 8;   // 8_8_8
            timing.tRTP = 4;
            timing.tWR = 8;

            timing.tRRD = 4;
            timing.tFAW = 20;
            timing.tRFC = (uint)Math.Ceiling(260.0 / timing.tCK);
            timing.tREFI = (ulong)Math.Floor(64000000 / 8192 / timing.tCK);
            timing.tRTRS = 2;
            timing.tRTW = (timing.tCL - timing.tCWL + timing.tCCD + 2);

            // subarray
            timing.tRA = (uint)Math.Ceiling(timing.tCL / 2.0);
            timing.tWA = timing.tCWL + timing.tBL + (uint)Math.Ceiling(timing.tWR / 2.0);
            timing.tSCD = 1;
        }

        private void DDR3_4Gb_x8_1333_10_10_10()
        {
            BANK_MAX = 8;   // fixed for DDR3
            ROW_MAX = 64 * 1024;
            COL_MAX = 8 * 1024 / 64;
            DEVICE_WIDTH = 8;
            DEVICES_PER_RANK = CHANNEL_WIDTH / DEVICE_WIDTH;

            timing.tCK = 1.5;  // ns

            timing.tRC = 34;
            timing.tRAS = 24;
            timing.tRP = 10;   // 10_10_10

            timing.tCCD = 4;
            timing.tWTR = 5;

            timing.tCL = 10;   // 10_10_10
            timing.tCWL = 7;
            timing.tBL = 4;

            timing.tRCD = 10;  // 10_10_10
            timing.tRTP = 5;
            timing.tWR = 10;

            timing.tRRD = 4;
            timing.tFAW = 20;
            timing.tRFC = (uint)Math.Ceiling(260.0 / timing.tCK);
            timing.tREFI = (ulong)Math.Floor(64000000 / 8192 / timing.tCK);
            timing.tRTRS = 2;
            timing.tRTW = (timing.tCL - timing.tCWL + timing.tCCD + 2);

            // subarray
            timing.tRA = (uint)Math.Ceiling(timing.tCL / 2.0);
            timing.tWA = timing.tCWL + timing.tBL + (uint)Math.Ceiling(timing.tWR / 2.0);
            timing.tSCD = 1;
        }

        private void DDR3_4Gb_x8_1600_11_11_11()
        {
            BANK_MAX = 8;   // fixed for DDR3
            ROW_MAX = 64 * 1024;
            COL_MAX = 8 * 1024 / 64;
            DEVICE_WIDTH = 8;
            DEVICES_PER_RANK = CHANNEL_WIDTH / DEVICE_WIDTH;

            timing.tCK = 1.25;  // ns

            timing.tRC = 39;
            timing.tRAS = 28;
            timing.tRP = 11;   // 11_11_11

            timing.tCCD = 4;
            timing.tWTR = 6;

            timing.tCL = 11;   // 11_11_11
            timing.tCWL = 8;
            timing.tBL = 4;

            timing.tRCD = 11;  // 11_11_11
            timing.tRTP = 6;
            timing.tWR = 12;

            timing.tRRD = 5;
            timing.tFAW = 24;
            timing.tRFC = (uint)Math.Ceiling(260.0 / timing.tCK);
            timing.tREFI = (ulong)Math.Floor(64000000 / 8192 / timing.tCK);
            timing.tRTRS = 2;
            timing.tRTW = (timing.tCL - timing.tCWL + timing.tCCD + 2);

            // subarray
            timing.tRA = (uint)Math.Ceiling(timing.tCL / 2.0);
            timing.tWA = timing.tCWL + timing.tBL + (uint)Math.Ceiling(timing.tWR / 2.0);
            timing.tSCD = 1;
        }

        private void DDR3_4Gb_x8_1866_13_13_13()
        {
            BANK_MAX = 8;   // fixed for DDR3
            ROW_MAX = 64 * 1024;
            COL_MAX = 8 * 1024 / 64;
            DEVICE_WIDTH = 8;
            DEVICES_PER_RANK = CHANNEL_WIDTH / DEVICE_WIDTH;

            timing.tCK = 1.07;  // ns

            timing.tRC = 46;
            timing.tRAS = 32;
            timing.tRP = 13;   // 13_13_13

            timing.tCCD = 4;
            timing.tWTR = 7;

            timing.tCL = 13;   // 13_13_13
            timing.tCWL = 8;
            timing.tBL = 4;

            timing.tRCD = 13;  // 13_13_13
            timing.tRTP = 7;
            timing.tWR = 14;

            timing.tRRD = 5;
            timing.tFAW = 28;
            timing.tRFC = (uint)Math.Ceiling(260.0 / timing.tCK);
            timing.tREFI = (ulong)Math.Floor(64000000 / 8192 / timing.tCK);
            timing.tRTRS = 2;
            timing.tRTW = (timing.tCL - timing.tCWL + timing.tCCD + 2);

            // subarray
            timing.tRA = (uint)Math.Ceiling(timing.tCL / 2.0);
            timing.tWA = timing.tCWL + timing.tBL + (uint)Math.Ceiling(timing.tWR / 2.0);
            timing.tSCD = 1;
        }

        public class Timing
        {
            /***** Timing Constraints *****/
            public double tCK; // clock cycle (ns)

            //----------------------------------------------------------//
            //-----Timing constraints between commands to SAME BANK-----//
            //----------------------------------------------------------//
            // Between row commands
            public uint tRC;   // ACTIVATE-to-ACTIVATE

            public uint tRAS;  // ACTIVATE-to-PRECHARGE
            public uint tRP;   // PRECHARGE-to-ACTIVATE

            // PRECHARGE-to-PRECHARGE (no constraint; can be issued consecutively)
            public uint tRP_rank; // precharge time for all banks within a rank

            // Between column commands
            public uint tCCD;  // READ-to-READ and WRITE-to-WRITE (tCCD >= tBL to avoid data bus conflict)

            public uint tRTW;  // READ-to-WRITE (function of other timing constraints: tCL-tCWL+tCCD+2)
            public uint tWTR;  // WRITE*-to-READ (*starts counting from first rising clock after last write data)

            // Between column command and first data
            public uint tCL;   // READ-to-DATA

            public uint tCWL;  // WRITE-to-DATA
            public uint tBL;   // DATA

            // Between row and column commands
            public uint tRCD;  // ACTIVATE-to-READ/WRITE

            public uint tRTP;  // READ-to-PRECHARGE
            public uint tWR;   // WRITE*-to-PRECHARGE (*starts counting from first rising clock after last write data)

            //----------------------------------------------------------------//
            //-----Timing constraints between commands to SAME RANK-----------//
            //----------------------------------------------------------------//
            // Between row commands
            public uint tRRD;  // ACTIVATE-to-ACTIVATE

            // ACTIVATE-to-PRECHARGE (no constraint; can be issued consecutively)
            // PRECHARGE-to-ACTIVATE (no constraint; can be issued consecutively)
            // PRECHARGE-to-PRECHARGE (no constraint; can be issued consecutively)
            public uint tFAW;  // Minimum time between five ACTIVATEs (subsumed by tRRD when 4 x tRRD >= tFAW)

            // Between column commands
            // READ-to-READ and WRITE-to-WRITE (same constraint as issuing commands to same bank)
            // READ-to-WRITE (same constraint as issuing commands to same bank)
            // WRITE*-to-READ (same constraint as issuing commands to same bank)

            // Between column command and first data (not applicable)

            // Between row and column commands
            // ACTIVATE-to-READ/WRITE (no constraint; can be issued consecutively)
            // READ-to-PRECHARGE (no constraint; can be issued consecutively)
            // WRITE-to-PRECHARGE (no constraint; can be issued consecutively)
            // READ/WRITE-to-ACTIVATE (no constraint; can be issued consecutively)

            public uint tRFC;  // REFRESH-to-ACTIVATE
            public ulong tREFI; // REFRESH-to-REFRESH (only a suggested constraint)
            public uint tRFCpb;   // per-bank refresh
            public ulong tREFIpb; // per-bank refresh interval

            //----------------------------------------------------------------//
            //-----Timing constraints between commands to DIFFERENT RANKS-----//
            //----------------------------------------------------------------//
            // Between row commands (no constraint; can be issued consecutively)

            // Between column commands (according to "DRAMSim2" from University of Maryland)
            public uint tRTRS;  // READ-to-READ and WRITE-to-WRITE (bubbles need be inserted in data bus, the number of which is tRTRS)

            // READ-to-WRITE (same constraint as issuing commands to same bank)
            // WRITE*-to-READ (same constraint as issuing commands to same bank)

            // Between column command and first data (not applicable)

            // Between row and column commands (no constraint; can be issued consecutively)

            //----------------------------------------------------------------//
            //-----Timing constraints for different subarrays-----------------//
            //----------------------------------------------------------------//
            public uint tRA;

            public uint tWA;
            public uint tSCD;

            //----------------------------------------------------------------//
            //-----Timing constraints (latency) for each copy command---------//
            //----------------------------------------------------------------//

            // RowClone (Seshadri et al., MICRO 2013)
            public uint tRC_INTRA_SA_COPY;

            public uint tRC_INTER_SA_COPY;
            public uint tRC_INTER_BANK_COPY;

            // LISA (Chang et al., HPCA 2016)
            public uint tLISA_INTER_SA_COPY;

            // Latency for a Row Buffer Movement (RBM)
            public uint tRBM;

            // A naive approach to copy data between two subarrays by using the
            // latency as specified in the JEDEC standards.
            public uint tNAIVE_BLOCK_INTER_SA_COPY;

            //----------------------------------------------------------------//
            //-----Scale Timing-----------------------------------------------//
            //----------------------------------------------------------------//
            public void Scale(uint clockFactor, uint colMax)
            {
                tRC *= clockFactor;
                tRAS *= clockFactor;
                tRP *= clockFactor;
                tRP_rank = (uint)(tRP * Config.mem.rank_to_bank_trp_ratio);

                tCCD *= clockFactor;
                tWTR *= clockFactor;

                tCL *= clockFactor;
                tCWL *= clockFactor;
                tBL *= clockFactor;

                tRCD *= clockFactor;
                tRTP *= clockFactor;
                tWR *= clockFactor;

                tRRD *= clockFactor;
                tFAW *= clockFactor;

                tRFC *= clockFactor;
                tREFI *= clockFactor;
                tRFCpb = (uint)(tRFC / Config.mem.rank_to_bank_trfc_ratio);
                tREFIpb = tREFI / Config.mem.bank_max;

                tRTRS *= clockFactor;
                tRTW *= clockFactor;

                //subarray
                tRA *= clockFactor;
                tWA *= clockFactor;
                tSCD *= clockFactor;

                // Latency of various copy commands
                tRBM = (uint)(Config.mctrl.lisa_rbm_latency / tCK * clockFactor);
                tRC_INTRA_SA_COPY = 2 * tRAS + tRP;
                tLISA_INTER_SA_COPY = tRAS + tRBM * (uint)Math.Ceiling((double)Config.mem.lisa_inter_sa_hop_count / 2) + tRP * 2 + tRAS * 2;
                tNAIVE_BLOCK_INTER_SA_COPY = tRCD + tCL + (colMax - 1) * tCCD + tRP + tRTW + tCWL + (colMax - 1) * tCCD + tBL + tWR + tRP;
                // Specify a latency value for copying data
                if (Config.mctrl.naive_block_copy_latency != -1)
                    tNAIVE_BLOCK_INTER_SA_COPY = (uint)Config.mctrl.naive_block_copy_latency;
                tRC_INTER_SA_COPY = tRCD + tCL + (colMax - 1) * tCCD + tRP + tRCD + tCWL + (colMax - 1) * tCCD + tWR + tRP;
                tRC_INTER_BANK_COPY = tRCD + tCL + (colMax - 1) * tCCD + tCWL + tWR + tRP;
            }

            public void update_lisa_clone_as_cache(uint newHopCount)
            {
                tLISA_INTER_SA_COPY = tRCD + tRBM * (uint)Math.Ceiling((double)newHopCount / 2) + tRP; // act + pre on source, then direct read from cache
            }
        }
    }
}