using Ramulator.MemCtrl;
using Ramulator.MemReq;
using Ramulator.Sim;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Ramulator
{
    public class Trace
    {
        public int pid;
        public bool finished;
        public string trace_fname;
        public int line_num;
        private GZipStream gzip_reader;

        // Size of buffer storing bytes read from the trace file
        public const int BUF_MAX = 1000;

        // For cache-unfiltered traces
        public ulong cur_inst_count;

        // The format is completely different from others
        public bool copy_trace_file;

        // Buffered writeback request used in the LLC shared cache mode where we consume L2-cache filtered traces.
        // We treat the wb address from L2 as a new write request.
        public double buffered_wb_addr;

        // This is for the baseline memcopy mechanism. We convert a copy request into a stream of memory requests.
        public Queue<ulong> copy_to_req_addr_q;

        public ulong copy_to_req_ipc_deduction;

        // Insert dirty
        public bool dirty_insert = false;

        public Trace(int pid, string trace_fname)
        {
            this.pid = pid;

            foreach (string dir in Config.TraceDirs.Split(',', ' '))
            {
                if (File.Exists(dir + "/" + trace_fname))
                {
                    trace_fname = dir + "/" + trace_fname;
                }
            }

            // Trace file
            Dbg.AssertPrint(File.Exists(trace_fname), trace_fname + " does not exist in the given paths.");
            this.trace_fname = trace_fname;

            gzip_reader = new GZipStream(File.OpenRead(trace_fname), CompressionMode.Decompress);

            cur_inst_count = 0;
            buffered_wb_addr = -1;

            if (trace_fname.IndexOf("-rc") != -1)
                copy_trace_file = true;
            else
                copy_trace_file = false;

            copy_to_req_addr_q = new Queue<ulong>(256);
            copy_to_req_ipc_deduction = 0;
        }

        private string read_gzip_trace()
        {
            byte[] single_buf = new byte[1];

            // EOF check
            if (gzip_reader.Read(single_buf, 0, 1) == 0)
                return null;

            byte[] buf = new byte[BUF_MAX];
            int n = 0;
            while (single_buf[0] != (byte)'\n')
            {
                buf[n++] = single_buf[0];
                gzip_reader.Read(single_buf, 0, 1);
            }
            return Encoding.ASCII.GetString(buf, 0, n);
        }

        private void get_req_clone(ref int cpu_inst_cnt, ref string line, Char[] delim, ref string[] tokens, out Req rd_req, out Req wb_req)
        {
            string inst_type = tokens[0];
            Dbg.Assert(copy_to_req_addr_q.Count == 0);
            if (inst_type == "R")
            {
                cpu_inst_cnt = int.Parse(tokens[3]);
                ulong rd_addr = ulong.Parse(tokens[1], System.Globalization.NumberStyles.HexNumber);
                //ulong rd_add_dup = rd_addr;
                rd_addr = rd_addr | (((ulong)pid) << 60);

                rd_req = RequestPool.Depool();
                rd_req.Set(pid, ReqType.READ, rd_addr);

                ulong wb_addr = ulong.Parse(tokens[2], System.Globalization.NumberStyles.HexNumber);
                wb_req = null;
                if (wb_addr != 0)
                {
                    wb_addr = wb_addr | (((ulong)pid) << 60);
                    if (Config.proc.llc_shared_cache_only)
                        buffered_wb_addr = wb_addr;
                    else if (Config.proc.wb)
                    {
                        wb_req = RequestPool.Depool();
                        wb_req.Set(pid, ReqType.WRITE, wb_addr);
                    }
                }
            }
            else if (inst_type == "C")
            {
                cpu_inst_cnt = 1;
                ulong dst_addr = ulong.Parse(tokens[1], System.Globalization.NumberStyles.HexNumber);
                ulong src_addr = ulong.Parse(tokens[2], System.Globalization.NumberStyles.HexNumber);
                dst_addr = dst_addr | (((ulong)pid) << 60);
                src_addr = src_addr | (((ulong)pid) << 60);

                // Convert the copy request into a list of RD/WR memory requests
                if (Config.mctrl.copy_method == COPY.MEMCPY)
                {
                    // Simply convert every memcopy to multiple read and write requests
                    Dbg.Assert(copy_to_req_addr_q.Count == 0);
                    // SRC and DST address
                    for (int i = 0; i < Config.mctrl.copy_gran; i++)
                    {
                        copy_to_req_addr_q.Enqueue(src_addr);
                        copy_to_req_addr_q.Enqueue(dst_addr);
                        // Increment by one cacheline
                        src_addr += 64;
                        dst_addr += 64;
                    }

                    cpu_inst_cnt = 1;
                    ulong rd_addr = copy_to_req_addr_q.Dequeue();
                    rd_req = RequestPool.Depool();
                    rd_req.Set(pid, ReqType.READ, rd_addr);
                    // For the destination addr, we need to mark it as dirty when the data is inserted back into the LLC.
                    rd_req.DirtyInsert = dirty_insert;
                    dirty_insert = !dirty_insert;
                    rd_req.CpyGenReq = Config.proc.stats_exclude_cpy;
                    wb_req = null;
                    Stat.banks[rd_req.Addr.cid, rd_req.Addr.rid, rd_req.Addr.bid].cmd_base_inter_sa.collect();
                    copy_to_req_ipc_deduction += 2;
                }
                else
                {
                    rd_req = RequestPool.Depool();
                    rd_req.Set(pid, ReqType.COPY, src_addr);
                    wb_req = null;
                }
            }
            else
            {
                rd_req = null;
                wb_req = null;
                Dbg.AssertPrint(inst_type == "C" || inst_type == "R", "Unable to fetch valid instruction.");
            }
        }

        // This mode is used when we use the L2-cache filtered trace files with shared LLC enabled
        private void get_llc_req(ref int cpu_inst_cnt, out Req rd_req, out Req wb_req)
        {
            // No buffered WB request from the previous line in the trace
            if (buffered_wb_addr == -1)
            {
                string line = read_trace();
                Char[] delim = new Char[] { ' ' };
                string[] tokens = line.Split(delim);

                // == Trace files that contain "clone" instructions ==
                if (copy_trace_file)
                {
                    // Skip all the other instructions
                    while (tokens[0] != "R" && tokens[0] != "C")
                    {
                        line = read_trace();
                        tokens = line.Split(delim);
                    }
                    get_req_clone(ref cpu_inst_cnt, ref line, delim, ref tokens, out rd_req, out wb_req);
                }
                // L2 cache filtered traces
                else
                {
                    cpu_inst_cnt = int.Parse(tokens[0]);
                    ulong rd_addr = ulong.Parse(tokens[1]);
                    rd_addr = rd_addr | (((ulong)pid) << 56);

                    rd_req = RequestPool.Depool();
                    rd_req.Set(pid, ReqType.READ, rd_addr);

                    wb_req = null;
                    if (!Config.proc.wb || tokens.Length == 2)
                        return;

                    Dbg.Assert(tokens.Length == 3);
                    ulong wb_addr = ulong.Parse(tokens[2]);
                    buffered_wb_addr = wb_addr | (((ulong)pid) << 56);
                }
            }
            else
            {
                // Use the buffered WB request
                cpu_inst_cnt = 0;
                rd_req = RequestPool.Depool();
                rd_req.Set(pid, ReqType.WRITE, (ulong)buffered_wb_addr);
                wb_req = null;
                // Clear the buffered wb
                buffered_wb_addr = -1;
            }
        }

        public void get_req(ref int cpu_inst_cnt, out Req rd_req, out Req wb_req)
        {
            if (copy_to_req_addr_q.Count > 0)
            {
                cpu_inst_cnt = 1;
                ulong rd_addr = copy_to_req_addr_q.Dequeue();
                rd_req = RequestPool.Depool();
                rd_req.Set(pid, ReqType.READ, rd_addr);
                // For the destination addr, we need to mark it as dirty when the data is inserted back into the LLC.
                rd_req.DirtyInsert = dirty_insert;
                dirty_insert = !dirty_insert;
                rd_req.CpyGenReq = Config.proc.stats_exclude_cpy;
                wb_req = null;
                copy_to_req_ipc_deduction += 2;
                return;
            }

            // Shared LLC mode on cache filtered traces
            if (Config.proc.llc_shared_cache_only)
            {
                get_llc_req(ref cpu_inst_cnt, out rd_req, out wb_req);
                return;
            }

            string line = read_trace();
            Char[] delim = new Char[] { ' ' };
            string[] tokens = line.Split(delim);

            // The format of cache unfiltered traces is different
            if (Config.proc.cache_enabled)
                get_req_cache_unfiltered(ref cpu_inst_cnt, ref line, delim, ref tokens, out rd_req, out wb_req);
            // traces with clones and sets
            else if (Config.proc.b_read_rc_traces && copy_trace_file)
            {
                // Skip all the other instructions
                while (tokens[0] != "R" && tokens[0] != "C")
                {
                    line = read_trace();
                    tokens = line.Split(delim);
                }
                get_req_clone(ref cpu_inst_cnt, ref line, delim, ref tokens, out rd_req, out wb_req);
            }
            // Cache filtered
            else
            {
                cpu_inst_cnt = int.Parse(tokens[0]);
                ulong rd_addr = ulong.Parse(tokens[1]);
                rd_addr = rd_addr | (((ulong)pid) << 56);

                rd_req = RequestPool.Depool();
                rd_req.Set(pid, ReqType.READ, rd_addr);

                if (!Config.proc.wb || tokens.Length == 2)
                {
                    wb_req = null;
                    return;
                }

                Dbg.Assert(tokens.Length == 3);
                ulong wb_addr = ulong.Parse(tokens[2]);
                wb_addr = wb_addr | (((ulong)pid) << 56);
                wb_req = RequestPool.Depool();
                wb_req.Set(pid, ReqType.WRITE, wb_addr);
            }
        }

        private void get_req_cache_unfiltered(ref int cpu_inst_cnt, ref string line, Char[] delim, ref string[] tokens, out Req rd_req, out Req wb_req)
        {
            Dbg.AssertPrint(tokens.Length == 6, "trace line = " + line);
            ReqType req_type = (int.Parse(tokens[5]) == 0) ? ReqType.READ : ReqType.WRITE;

            // Read-only requests
            while (Config.proc.wb == false && req_type == ReqType.WRITE)
            {
                line = read_trace();
                tokens = line.Split(delim);
                req_type = (int.Parse(tokens[5]) == 0) ? ReqType.READ : ReqType.WRITE;
            }

            // Set instruction count b/w requests
            ulong icount = ulong.Parse(tokens[0]);
            if (cur_inst_count == 0)
                cpu_inst_cnt = 0;
            else
            {
                cpu_inst_cnt = (int)(icount - cur_inst_count);
                Dbg.AssertPrint(cpu_inst_cnt >= 0, "Negative instruction count");
            }
            cur_inst_count = icount;

            // Parse virtual address
            ulong vaddr = ulong.Parse(tokens[2]);
            vaddr = vaddr + (((ulong)pid) << 48);
            rd_req = RequestPool.Depool();
            rd_req.Set(pid, req_type, vaddr);
            wb_req = null;
        }

        public string read_trace()
        {
            line_num++;
            string line = read_gzip_trace();
            if (line != null)
                return line;

            //reached EOF; reopen trace file
            finished = true;
            gzip_reader.Close();
            gzip_reader = new GZipStream(File.OpenRead(trace_fname), CompressionMode.Decompress);
            cur_inst_count = 0;

            line_num = 0;
            line = read_trace();
            Dbg.Assert(line != null);
            return line;
        }
    }
}