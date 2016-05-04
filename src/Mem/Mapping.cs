using System;

namespace Ramulator.Mem
{
    public class MemAddr
    {
        public uint cid;
        public uint rid;
        public uint bid;
        public uint said;
        public ulong rowid;
        public uint colid;
        public MemAddr() { }

        public MemAddr(MemAddr addr)
        {
            cid = addr.cid;
            rid = addr.rid;
            bid = addr.bid;
            said = addr.said;
            rowid = addr.rowid;
            colid = addr.colid;
        }

        public void Reset()
        {
            cid = 0;
            rid = 0;
            bid = 0;
            said = 0;
            rowid = 0;
            colid = 0;
        }
    }

    public static class MemMap
    {
        public enum MapEnum
        {
            /* Misc. Interleavings */
            ROW_BANK1_COL_BANK2_RANK_CHAN,

            /* Subarray-Aware Interleavings */
            // inter-channel
            ROW_COL_SA_BANK_RANK_CHAN,      // line-interleaving (BANK_RANK)
            ROW_COL_SA_RANK_BANK_CHAN,      // line-interleaving (RANK_BANK)
            ROW_SA_RANK_BANK_COL_CHAN,      // line-interleaving
            SA_ROW_RANK_BANK_COL_CHAN,      // line-interleaving
            
            // TODO:
            ROW_SA_RANK_BANK_CHAN_COL,      // row-interleaving
            ROW_RANK_BANK_SA_COL_CHAN,      // line-interleaving
            ROW_RANK_SA_BANK_COL_CHAN,      // line-interleaving

            /* BANK_RANK Interleavings */
            // inter-channel
            ROW_BANK_RANK_CHAN_COL,         // row-interleaving (*Used for SALP*)
            ROW_BANK_RANK_COL_CHAN,         // line-interleaving
                                            // intermediate-interleaving
            ROW_RANK_COL_BANK_CHAN,         // line-interleaving
            ROW_RANK_COL1_BANK_COL2_CHAN,   // line-interleaving


            // inter-channel/brank
            ROW_COL_BANK_RANK_CHAN,         // line-interleaving (*Used for SALP*)

            /* RANK_BANK Interleavings */
            // inter-channel
            ROW_RANK_BANK_CHAN_COL,         // row-interleaving
            ROW_RANK_BANK_COL_CHAN,         // line-interleaving
            ROW_RANK_BANK_COL1_CHAN_COL2,   // intermediate-interleaving

            // inter-rbank
            ROW_CHAN_RANK_BANK_COL,         // row-interleaving
            ROW_CHAN_COL_RANK_BANK,         // line-interleaving
            ROW_CHAN_COL1_RANK_BANK_COL2,   // intermediate-interleaving

            // inter-channel/rbank
            ROW_COL_RANK_BANK_CHAN,         // line-interleaving
            ROW_COL1_RANK_BANK_CHAN_COL2,   // intermediate-interleaving

            // inter-rbank/channel
            ROW_COL_CHAN_RANK_BANK,         // line-interleaving
            ROW_COL1_CHAN_RANK_BANK_COL2,   // intermediate-interleaving
        }
        public static MapEnum MapType;
        public static uint ChannelMax;

        //bits
        private static uint _chanBits;
        private static uint _rankBits;
        private static uint _bankBits;
        private static uint _bank1Bits;
        private static uint _bank2Bits;
        private static uint _colBits;
        private static uint _col1Bits;
        private static uint _col2Bits;

        private static uint _subarrayBits;
        private static uint _transferBits;

        //offset
        private static uint _chanOffset;
        private static uint _rankOffset;
        private static uint _bankOffset;
        private static uint _bank1Offset;
        private static uint _bank2Offset;
        private static uint _rowOffset;
        private static uint _colOffset;
        private static uint _col1Offset;
        private static uint _col2Offset;

        private static uint _subarrayOffset;

        //masks
        private static ulong _chanMask;
        private static ulong _rankMask;
        private static ulong _bankMask;
        private static ulong _bank1Mask;
        private static ulong _bank2Mask;
        //row_mask is not needed (it consists of *all* MSbits)
        private static ulong _colMask;
        private static ulong _col1Mask;
        private static ulong _col2Mask;

        private static ulong _subarrayMask;

        public static uint GetRowOffset()
        {
            return _rowOffset;
        }

        //constructor
        public static void Init(MapEnum mapType, uint channelMax, uint rankMax, uint colPerSubrow, DDR3DRAM ddr3)
        {
            MapType = mapType;
            ChannelMax = channelMax;

            /* number of bits in index */
            //channel
            _chanBits = (uint)Math.Log(channelMax, 2);

            //rank
            _rankBits = (uint)Math.Log(rankMax, 2);

            //bank
            _bankBits = (uint)Math.Log(ddr3.BANK_MAX, 2);
            if (mapType == MapEnum.ROW_BANK1_COL_BANK2_RANK_CHAN) {
                _bank2Bits = 3;
                _bank1Bits = _bankBits - 3;
            }

            //column
            _colBits = (uint)Math.Log(ddr3.COL_MAX, 2);
            if (colPerSubrow > 0) {
                _col2Bits = (uint)Math.Log(colPerSubrow, 2);
                _col1Bits = _colBits - _col2Bits;
            }
            else {
                _col2Bits = 0;
            }

            //row
            _subarrayBits = (uint)Math.Log(ddr3.SUBARRAYS_PER_BANK, 2);

            //transfer
            _transferBits = (uint)Math.Log(64, 2); //64B transfer


            /* bitmask and bitoffset for each index */
            set_maskoffset();
        }

        //MemoryAddress
        public static MemAddr Translate(ulong paddr)
        {
            MemAddr addr = new MemAddr();

            /* step 1: channel index */
            addr.cid = (uint)((paddr & _chanMask) >> (int)_chanOffset);

            /* step 2: rank index */
            addr.rid = (uint)((paddr & _rankMask) >> (int)_rankOffset);

            /* step 3: bank index */
            if (_bank2Bits == 0)
            {
                addr.bid = (uint)((paddr & _bankMask) >> (int)_bankOffset);
            }
            else
            {
                // special provisioning: split bank index */
                uint bid2 = (uint)((paddr & _bank2Mask) >> (int)_bank2Offset);
                uint bid1 = (uint)((paddr & _bank1Mask) >> (int)_bank1Offset);
                uint bid = bid2 + (bid1 << (int)_bank2Bits);
                addr.bid = bid;
            }

            /* step 4: row index (no mask, comes from MSb) */
            addr.rowid = paddr >> (int)_rowOffset;

            /* step 5: column index */
            if (_col2Bits == 0)
            {
                addr.colid = (uint)((paddr & _colMask) >> (int)_colOffset);
            }
            else
            {
                // special provisioning for split column index
                uint col2id = (uint)((paddr & _col2Mask) >> (int)_col2Offset);
                uint col1id = (uint)((paddr & _col1Mask) >> (int)_col1Offset);
                uint colid = col2id + (col1id << (int)_col2Bits);
                addr.colid = colid;
            }

            /* step 6: subarray index */
            if (_subarrayOffset == 0)
            {
                // default: subarray index is LSb of row index
                _subarrayMask = (1UL << (int)_subarrayBits) - 1;
                addr.said = (uint)(addr.rowid & _subarrayMask);
            }
            else if (_subarrayBits != 0)
            {
                // special provisioning: subarray index at specified location
                addr.said = (uint)((paddr & _subarrayMask) >> (int)_subarrayOffset);
                
                // CAUTION: subarray index must be included in the row index.
                // This is because row-buffer hit status is determined solely
                // on the row index at the granularity of a bank in the
                // baseline system without SALP. If sa index is not included,
                // accessing two different rows with the same row index in
                // different subarrays within the same bank would be considered
                // as a row hit.
                addr.rowid <<= (int)_subarrayBits;
                addr.rowid += addr.said;
            }

            /* step 7: we're done */
            return addr;
        }

        private static void set_maskoffset(ref ulong mask, uint bits, ref uint offset, ref uint currOffset)
        {
            offset = currOffset;
            mask = 1;
            mask = (mask << (int) bits) - 1;
            mask <<= (int) offset;

            currOffset += bits;
        }

        private static void Chan(ref uint currOffset)
        {
            set_maskoffset(ref _chanMask, _chanBits, ref _chanOffset, ref currOffset);
        }
        private static void Rank(ref uint currOffset)
        {
            set_maskoffset(ref _rankMask, _rankBits, ref _rankOffset, ref currOffset);
        }
        private static void Bank(ref uint currOffset)
        {
            set_maskoffset(ref _bankMask, _bankBits, ref _bankOffset, ref currOffset);
        }
        private static void Bank1(ref uint currOffset)
        {
            set_maskoffset(ref _bank1Mask, _bank1Bits, ref _bank1Offset, ref currOffset);
        }
        private static void Bank2(ref uint currOffset)
        {
            set_maskoffset(ref _bank2Mask, _bank2Bits, ref _bank2Offset, ref currOffset);
        }
        private static void Row(ref uint currOffset)
        {
            _rowOffset = currOffset;
        }
        private static void Col(ref uint currOffset)
        {
            set_maskoffset(ref _colMask, _colBits, ref _colOffset, ref currOffset);
        }
        private static void Col1(ref uint currOffset)
        {
            set_maskoffset(ref _col1Mask, _col1Bits, ref _col1Offset, ref currOffset);
        }
        private static void Col2(ref uint currOffset)
        {
            set_maskoffset(ref _col2Mask, _col2Bits, ref _col2Offset, ref currOffset);
        }
        private static void Sa(ref uint currOffset)
        {
            set_maskoffset(ref _subarrayMask, _subarrayBits, ref _subarrayOffset, ref currOffset);
        }

        private static void set_maskoffset()
        {
            //transfer offset
            uint currOffset = _transferBits;

            //map type
            switch (MapType) {
                /* Misc. Interleavings */
                case MapEnum.ROW_BANK1_COL_BANK2_RANK_CHAN:
                    Chan(ref currOffset);
                    Rank(ref currOffset);
                    Bank2(ref currOffset);
                    Col(ref currOffset);
                    Bank1(ref currOffset);
                    Row(ref currOffset);
                    break;


                /* Subarray-Aware Interleavings */
                // inter-channel
                case MapEnum.ROW_COL_SA_BANK_RANK_CHAN:
                    Chan(ref currOffset);
                    Rank(ref currOffset);
                    Bank(ref currOffset);
                    Sa(ref currOffset);
                    Col(ref currOffset);
                    Row(ref currOffset);
                    break;
                case MapEnum.ROW_COL_SA_RANK_BANK_CHAN:
                    Chan(ref currOffset);
                    Bank(ref currOffset);
                    Rank(ref currOffset);
                    Sa(ref currOffset);
                    Col(ref currOffset);
                    Row(ref currOffset);
                    break;
                case MapEnum.SA_ROW_RANK_BANK_COL_CHAN:
                    Chan(ref currOffset);
                    Col(ref currOffset);
                    Bank(ref currOffset);
                    Rank(ref currOffset);
                    Row(ref currOffset);
                    Sa(ref currOffset);
                    break;
                case MapEnum.ROW_SA_RANK_BANK_COL_CHAN:
                    Chan(ref currOffset);
                    Col(ref currOffset);
                    Bank(ref currOffset);
                    Rank(ref currOffset);
                    Sa(ref currOffset);
                    Row(ref currOffset);
                    break;
                
                /* BANK_RANK Interleavings */
                //inter-channel
                case MapEnum.ROW_BANK_RANK_CHAN_COL:
                    Col(ref currOffset);
                    Chan(ref currOffset);
                    Rank(ref currOffset);
                    Bank(ref currOffset);
                    Row(ref currOffset);
                    break;
                case MapEnum.ROW_BANK_RANK_COL_CHAN:
                    Chan(ref currOffset);
                    Col(ref currOffset);
                    Rank(ref currOffset);
                    Bank(ref currOffset);
                    Row(ref currOffset);
                    break;
                case MapEnum.ROW_RANK_COL_BANK_CHAN:
                    Chan(ref currOffset);
                    Bank(ref currOffset);
                    Col(ref currOffset);
                    Rank(ref currOffset);
                    Row(ref currOffset);
                    break;
                case MapEnum. ROW_RANK_COL1_BANK_COL2_CHAN:
                    Chan(ref currOffset);
                    Col2(ref currOffset);
                    Bank(ref currOffset);
                    Col1(ref currOffset);
                    Rank(ref currOffset);
                    Row(ref currOffset);
                    break;

                // inter-channel/brank
                case MapEnum.ROW_COL_BANK_RANK_CHAN:
                    Chan(ref currOffset);
                    Rank(ref currOffset);
                    Bank(ref currOffset);
                    Col(ref currOffset);
                    Row(ref currOffset);
                    break;


                /* RANK_BANK Interleavings */
                //inter-channel
                case MapEnum.ROW_RANK_BANK_CHAN_COL:
                    Col(ref currOffset);
                    Chan(ref currOffset);
                    Bank(ref currOffset);
                    Rank(ref currOffset);
                    Row(ref currOffset);
                    break;
                case MapEnum.ROW_RANK_BANK_COL_CHAN:
                    Chan(ref currOffset);
                    Col(ref currOffset);
                    Bank(ref currOffset);
                    Rank(ref currOffset);
                    Row(ref currOffset);
                    break;
                case MapEnum.ROW_RANK_BANK_COL1_CHAN_COL2:
                    Col2(ref currOffset);
                    Chan(ref currOffset);
                    Col1(ref currOffset);
                    Bank(ref currOffset);
                    Rank(ref currOffset);
                    Row(ref currOffset);
                    break;

                //inter-bank
                case MapEnum.ROW_CHAN_RANK_BANK_COL:
                    Col(ref currOffset);
                    Bank(ref currOffset);
                    Rank(ref currOffset);
                    Chan(ref currOffset);
                    Row(ref currOffset);
                    break;
                case MapEnum.ROW_CHAN_COL_RANK_BANK:
                    Bank(ref currOffset);
                    Rank(ref currOffset);
                    Col(ref currOffset);
                    Chan(ref currOffset);
                    Row(ref currOffset);
                    break;
                case MapEnum.ROW_CHAN_COL1_RANK_BANK_COL2:
                    Col2(ref currOffset);
                    Bank(ref currOffset);
                    Rank(ref currOffset);
                    Col1(ref currOffset);
                    Chan(ref currOffset);
                    Row(ref currOffset);
                    break;

                //inter-channel/rbank
                case MapEnum.ROW_COL_RANK_BANK_CHAN:
                    Chan(ref currOffset);
                    Bank(ref currOffset);
                    Rank(ref currOffset);
                    Col(ref currOffset);
                    Row(ref currOffset);
                    break;
                case MapEnum.ROW_COL1_RANK_BANK_CHAN_COL2:
                    Col2(ref currOffset);
                    Chan(ref currOffset);
                    Bank(ref currOffset);
                    Rank(ref currOffset);
                    Col1(ref currOffset);
                    Row(ref currOffset);
                    break;

                //inter-rbank/channel
                case MapEnum.ROW_COL_CHAN_RANK_BANK:
                    Bank(ref currOffset);
                    Rank(ref currOffset);
                    Chan(ref currOffset);
                    Col(ref currOffset);
                    Row(ref currOffset);
                    break;
                case MapEnum.ROW_COL1_CHAN_RANK_BANK_COL2:
                    Col2(ref currOffset);
                    Bank(ref currOffset);
                    Rank(ref currOffset);
                    Chan(ref currOffset);
                    Col1(ref currOffset);
                    Row(ref currOffset);
                    break;
            }
        }
    }
}
