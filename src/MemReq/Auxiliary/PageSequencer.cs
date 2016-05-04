using System;
using System.Collections.Generic;
using Ramulator.Sim;

namespace Ramulator.MemReq.Auxiliary
{
    public class PageSequencer
    {
        public ulong PageSize;
        public uint Cmax;
        public uint Rmax;
        public uint Bmax;
        public uint Stride;

        public Dictionary<ulong, ulong> Ptable; //pages
        public List<ulong> Ftable;              //frames

        public ulong CurrFid;

        public Random Rand = new Random(0);

        public PageSequencer(ulong pageSize, uint cmax, uint rmax, uint bmax)
        {
            PageSize = pageSize;
            Cmax = cmax;
            Rmax = rmax;
            Bmax = bmax;
            Stride = cmax * rmax * bmax;
            Ptable = new Dictionary<ulong, ulong>();
            Ftable = new List<ulong>();
        }

        public ulong get_paddr(ulong paddr)
        {
            ulong pageId = paddr / PageSize;
            ulong pageMod = paddr % PageSize;

            //page table hit
            if (Ptable.ContainsKey(pageId))
            {
                return Ptable[pageId] * PageSize + pageMod;
            }

            //page table miss
            ulong frameId = pageId / Stride;
            frameId *= Stride;
            frameId += CurrFid;

            //update tables
            // TODO: there's a bug in this allocator b/c there can be duplicate frames
            Dbg.Assert(!Ftable.Contains(frameId));
            Ftable.Add(frameId);
            Ptable.Add(pageId, frameId);

            //update frame id
            CurrFid += 1;
            CurrFid = CurrFid % Stride;

            //return physical address
            return frameId * PageSize + pageMod;
        }
    }
}