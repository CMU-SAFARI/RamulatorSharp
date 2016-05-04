using Ramulator.Sim;

namespace Ramulator
{
    public class InstWnd
    {
        public int size;
        public int load;           //number of instructions currently in the window

        public int oldest;         //index of the oldest instruction in the window
        public int next;           //index of the next empty slot in the window

        public ulong[] addr;       //array of instructions (memory request block addresses)
        public bool[] ready;       //array of instructions (whether they are ready)
        public bool[] is_mem;
        public bool[] is_write;
        public bool[] is_copy;
        public int[] word_offset;

        public InstWnd(int size)
        {
            this.size = size + 1;
            next = oldest = 0;

            addr = new ulong[size + 1];
            ready = new bool[size + 1];
            is_mem = new bool[size + 1];
            is_write = new bool[size + 1];
            is_copy = new bool[size + 1];
            word_offset = new int[size + 1];
            for (int i = 0; i < size + 1; i++)
            {
                ready[i] = true;
                is_write[i] = false;
            }
        }

        public bool is_full()
        {
            return load == size - 1;
        }

        public bool is_empty()
        {
            return load == 0;
        }

        public void add(ulong block_addr, bool is_mem_inst, bool is_ready, int w_offset, bool write = false, bool copy = false)
        {
            Dbg.Assert(load < size - 1);
            load++;

            addr[next] = block_addr;
            ready[next] = is_ready;
            is_mem[next] = is_mem_inst;
            is_write[next] = write;
            is_copy[next] = copy;
            word_offset[next] = w_offset;

            next = (next + 1) % size;
        }

        public ulong head()
        {
            return addr[oldest];
        }

        public int retire(int n)
        {
            int retired = 0;

            while (oldest != next && retired < n)
            {
                if (!ready[oldest])
                    break;

                oldest = (oldest + 1) % size;
                load--;
                retired++;
            }

            return retired;
        }

        public bool is_duplicate(ulong block_addr)
        {
            int count = 0;
            int i = oldest;
            while (i != next)
            {
                if (is_mem[i] && addr[i] == block_addr && !is_copy[i])
                {
                    if (++count > 1)
                    {
                        return true;
                    }
                }
                i = (i + 1) % size;
            }
            return false;
        }

        public int dup_req_count(ulong block_addr)
        {
            int count = 0;
            int i = oldest;
            while (i != next)
            {
                if (is_mem[i] && addr[i] == block_addr)
                {
                    count++;
                }
                i = (i + 1) % size;
            }
            return count;
        }

        public bool set_ready(ulong block_addr, bool set_copy_ready = false)
        {
            int i = oldest;
            bool write = false;
            while (i != next)
            {
                if (is_mem[i] && addr[i] == block_addr && is_copy[i] == set_copy_ready)
                {
                    is_mem[i] = false;
                    ready[i] = true;
                    addr[i] = 0;
                    // Mainly used when cache is enabled
                    if (is_write[i])
                        write = true;
                    is_write[i] = false;
                }
                i = (i + 1) % size;
            }
            return write;
        }

        public bool is_oldest_ready()
        {
            return ready[oldest];
        }
    }
}