using Ramulator.Sim;

namespace Ramulator.Mem
{
    public class BusStat : StatGroup
    {
        //id
        public uint cid;

        //service
        public AccumRateStat utilization;

        //accesses
        public AccumStat access;

        public BusStat(uint cid)
        {
            this.cid = cid;
            Init();
        }
    }
}