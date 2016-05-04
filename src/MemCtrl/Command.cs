using Ramulator.Mem;
using System;
using Ramulator.MemReq;

namespace Ramulator.MemCtrl
{
    public enum CmdType
    {
        ACT = 0,
        SEL_SA,
        PRE_RANK,
        PRE_BANK,
        PRE_SA,
        REF_RANK,
        REF_BANK,
        RD,
        WR,
        RD_AP,
        WR_AP,
        ROWCLONE_INTRA_SA_COPY,
        ROWCLONE_INTER_SA_COPY,
        ROWCLONE_INTER_BANK_COPY,
        LINKS_INTER_SA_COPY,
        BASE_INTER_SA_COPY,
        MAX
    }

    public class Cmd
    {
        public MemAddr Addr;
        public CmdType Type;
        public Req Req;

        //constructor
        public Cmd()
        {
        }

        public Cmd(CmdType type, MemAddr addr, Req req)
        {
            Addr = addr;
            Type = type;
            Req = req;
        }

        public bool is_column()
        {
            switch (Type)
            {
                case CmdType.RD:
                case CmdType.WR:
                case CmdType.RD_AP:
                case CmdType.WR_AP:
                    return true;

                default:
                    return false;
            }
        }

        public bool is_read()
        {
            switch (Type)
            {
                case CmdType.RD:
                case CmdType.RD_AP:
                    return true;

                default:
                    return false;
            }
        }

        public bool is_write()
        {
            switch (Type)
            {
                case CmdType.WR:
                case CmdType.WR_AP:
                    return true;

                default:
                    return false;
            }
        }

        public bool is_rank()
        {
            switch (Type)
            {
                case CmdType.PRE_RANK:
                case CmdType.REF_RANK:
                    return true;

                default:
                    return false;
            }
        }

        public bool is_refresh()
        {
            switch (Type)
            {
                case CmdType.REF_RANK:
                case CmdType.REF_BANK:
                    return true;

                default:
                    return false;
            }
        }

        public bool is_copy()
        {
            switch (Type)
            {
                case CmdType.ROWCLONE_INTRA_SA_COPY:
                case CmdType.ROWCLONE_INTER_SA_COPY:
                case CmdType.ROWCLONE_INTER_BANK_COPY:
                case CmdType.LINKS_INTER_SA_COPY:
                case CmdType.BASE_INTER_SA_COPY:
                    return true;

                default:
                    return false;
            }
        }

        public string to_str()
        {
            return String.Format("{0,10} PID{1} C{2} R{3} B{4} S{5} ROW{6} COL{7}", Type.ToString(), Req.Pid, Addr.cid, Addr.rid, Addr.bid, Addr.said, Addr.rowid, Addr.colid);
        }
    }
}