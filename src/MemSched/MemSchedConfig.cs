using System;
using Ramulator.Sim;

namespace Ramulator.MemSched
{
    public class MemSchedConfig : ConfigGroup
    {
        // read scheduling algorithm
        public string sched_algo = "FRFCFS";

        public Type typeof_sched_algo;

        // write scheduling algorithm
        public string wbsched_algo = "FRFCFS";

        public bool tcm_only_rmpki = false;
        public Type typeof_wbsched_algo;

        //prioritize row-hits
        public bool prioritize_row_hits = false;

        /*************************
         * FRFCFS Scheduler
         *************************/
        public int row_hit_cap = 4;

        /*************************
         * STFM Scheduler
         *************************/
        public double alpha = 1.1;
        public ulong beta = 1048576;
        public double gamma = 0.5;
        public int ignore_gamma = 0;

        /*************************
         * ATLAS Scheduler
         *************************/
        public int quantum_cycles = 1000000;
        public int threshold_cycles = 100000;
        public double history_weight = 0.875;

        /*************************
         * PAR-BS Scheduler
         *************************/
        public int batch_cap = 5;
        public int prio_max = 11;   //0~9 are real priorities, 10 is no-priority

        //schedulers: FR_FCFS_Cap, NesbitFull
        public ulong prio_inv_thresh = 0;

        //schedulers: STFM, Nesbit{Basic, Full}
        public int use_weights = 0;

        public double[] weights = new double[128];

        /*************************
         * TCM Scheduler
         *************************/
        public double AS_cluster_factor = 0.10;

        //shuffle
        public TCM.ShuffleAlgo shuffle_algo = TCM.ShuffleAlgo.Hanoi;

        public int shuffle_cycles = 800;
        public bool is_adaptive_shuffle = true;
        public double adaptive_threshold = 0.1;

        /*************************
         * BLISS
         *************************/
        public int bliss_shuffle_cycles = 10000;
        public int bliss_row_hit_cap = 4;
        public int frfcfs_qos_threshold = 2000;

        protected override bool set_special_param(string param, string val)
        {
            return false;
        }

        public override void finalize()
        {
            //memory scheduling algo
            string type_name = typeof(MemSched).Namespace + "." + Config.sched.sched_algo;
            try
            {
                typeof_sched_algo = Type.GetType(type_name);
            }
            catch
            {
                throw new Exception(String.Format("Scheduler not found {0}", Config.sched.sched_algo));
            }

            type_name =  typeof(MemSched).Namespace + "." + Config.sched.wbsched_algo;
            try
            {
                typeof_wbsched_algo = Type.GetType(type_name);
            }
            catch
            {
                throw new Exception(String.Format("Writeback scheduler not found {0}", Config.sched.wbsched_algo));
            }
        }
    }
}