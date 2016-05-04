using System.Collections.Generic;

namespace Ramulator.MemReq.Auxiliary
{
    // Sequentially allocate physical frames
    public class ContiguousAllocator
    {
        private ulong _pageSize;
        private Dictionary<ulong, ulong> _ptable; //pages
        private ulong _phyPageNumber;
        private ulong _dramMaxRowCnt; // Maximum number of rows in this DRAM system

        public ContiguousAllocator(ulong pageSize, ulong dramMaxRowCnt)
        {
            _phyPageNumber = 0;
            _pageSize = pageSize;
            _ptable = new Dictionary<ulong, ulong>();
            _dramMaxRowCnt = dramMaxRowCnt;
        }

        public ulong get_paddr(ulong paddr)
        {
            ulong pageId = paddr / _pageSize;
            ulong pageMod = paddr % _pageSize;

            //page table hit
            if (_ptable.ContainsKey(pageId))
                return _ptable[pageId] * _pageSize + pageMod;

            //page table miss
            _ptable.Add(pageId, _phyPageNumber);
            ulong phyAddr = _phyPageNumber * _pageSize + pageMod;
            _phyPageNumber++;
            if (_phyPageNumber == _dramMaxRowCnt)
                throw new System.Exception("Error: Cannot allocate more physical pages. The system has reached its maximum row count.");
            return phyAddr;
        }
    }
}