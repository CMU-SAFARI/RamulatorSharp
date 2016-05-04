using System;
using System.Collections.Generic;
using Ramulator.Sim;

namespace Ramulator.MemReq.Auxiliary
{
    public class PageRandomizer
    {
        public ulong PageSize;
        public Dictionary<ulong, ulong> Ptable; //pages
        public List<ulong> Ftable;              //frames
        public int[, , ,] SaRowCount;
        public int RowMaxPerSubarray;

        public Random Rand = new Random(0);

        public PageRandomizer(ulong pageSize, uint rowMax)
        {
            PageSize = pageSize;
            Ptable = new Dictionary<ulong, ulong>();
            Ftable = new List<ulong>();

            SaRowCount = new int[Config.mem.chan_max, Config.mem.rank_max, Config.mem.bank_max, Config.mem.subarray_max];
            RowMaxPerSubarray = (int)(rowMax / Config.mem.bank_max / Config.mem.subarray_max);

            for (int c = 0; c < Config.mem.chan_max; c++)
                for (int r = 0; r < Config.mem.rank_max; r++)
                    for (int b = 0; b < Config.mem.bank_max; b++)
                        for (int s = 0; s < Config.mem.subarray_max; s++)
                            SaRowCount[c, r, b, s] = 0;
        }

        public ulong get_paddr(ulong paddr)
        {
            ulong pageId = paddr / PageSize;
            ulong pageMod = paddr % PageSize;

            if (Ptable.ContainsKey(pageId))
            {
                return Ptable[pageId] * PageSize + pageMod;
            }

            ulong frameId;
            while (true)
            {
                frameId = (ulong)Rand.Next();
                if (Ftable.Contains(frameId))
                {
                    continue;
                }

                Ftable.Add(frameId);
                break;
            }

            Ptable.Add(pageId, frameId);
            return frameId * PageSize + pageMod;
        }
    }
}