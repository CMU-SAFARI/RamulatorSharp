using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Ramulator.Mem;
using Ramulator.MemCtrl;
using Ramulator.MemSched;
using Ramulator.Proc;

namespace Ramulator.Sim
{
    public abstract class StatGroup
    {
        public void DumpJSON(TextWriter tw)
        {
            tw.WriteLine("{");

            bool first = true;
            foreach (FieldInfo fi in GetType().GetFields())
            {
                object o = fi.GetValue(this);
                if (!first) tw.WriteLine(",");
                tw.Write("\"{0}\":", fi.Name);
                DumpJSON(tw, o);
                first = false;
            }

            tw.WriteLine("\n}");
        }

        public void DumpJSON(TextWriter tw, object o)
        {
            if (o is StatObject)
            {
                ((StatObject)o).DumpJSON(tw);
            }
            else if (o is string)
            {
                tw.Write("\"{0}\"", (string)o);
            }
            else if (o is object[])
            {
                bool first = true;
                tw.Write("[");
                foreach (object elem in (object[])o)
                {
                    if (first) first = false;
                    else tw.Write(",");
                    DumpJSON(tw, elem);
                }
                tw.Write("]");
            }
            else if (o is object[,])
            {
                object[,] arr_2d = (object[,])o;
                int dim0 = arr_2d.GetLength(0);
                int dim1 = arr_2d.GetLength(1);
                object[][] arr = new object[dim0][];
                for (int i = 0; i < dim0; i++)
                {
                    arr[i] = new object[dim1];
                    for (int j = 0; j < dim1; j++)
                        arr[i][j] = arr_2d[i, j];
                }
                DumpJSON(tw, arr);
            }
            else if (o is object)
            {
                tw.Write("{0}", o.ToString());
            }
        }

        // Finisher
        public void Finish(ulong time)
        {
            foreach (FieldInfo fi in GetType().GetFields())
            {
                if (fi.GetValue(this) == null)
                    continue;

                object o = fi.GetValue(this);
                if (o is StatObject)
                {
                    ((StatObject)o).Finish(time);
                }
                else if (o is StatObject[])
                {
                    foreach (StatObject elem in ((StatObject[])o))
                    {
                        elem.Finish(time);
                    }
                }
                else if (o is StatObject[,])
                {
                    foreach (StatObject elem in ((StatObject[,])o))
                    {
                        elem.Finish(time);
                    }
                }
            }
        }

        // Reset stats
        public void Reset()
        {
            foreach (FieldInfo fi in GetType().GetFields())
            {
                if (fi.GetValue(this) == null)
                    continue;

                object o = fi.GetValue(this);
                if (o is StatObject)
                {
                    ((StatObject)o).Reset();
                }
                else if (o is StatObject[])
                {
                    foreach (StatObject elem in ((StatObject[])o))
                    {
                        elem.Reset();
                    }
                }
                else if (o is StatObject[,])
                {
                    foreach (StatObject elem in ((StatObject[,])o))
                    {
                        elem.Reset();
                    }
                }
            }
        }

        public void Finish(ulong time, int idx)
        {
            foreach (FieldInfo fi in GetType().GetFields())
            {
                if (fi.GetValue(this) == null)
                    continue;

                object o = fi.GetValue(this);
                if (o is StatObject[])
                {
                    ((StatObject[])o)[idx].Finish(time);
                }
                else if (o is StatObject[,])
                {
                    for (int j = 0; j < ((StatObject[,])o).GetLength(1); j++)
                        ((StatObject[,])o)[idx, j].Finish(time);
                }
            }
        }

        //initializer
        protected void Init()
        {
            foreach (FieldInfo fi in GetType().GetFields())
            {
                if (fi.GetValue(this) != null)
                    continue;

                Type t = fi.FieldType;
                if (t == typeof(AccumStat))
                    fi.SetValue(this, newAccumStat());
                else if (t == typeof(AccumStat[]))
                    fi.SetValue(this, newAccumStatArray(Config.N));
                else if (t == typeof(AccumStat[,]))
                    fi.SetValue(this, newAccumStatArray2D(Config.N, Config.N));
                else if (t == typeof(AccumRateStat))
                    fi.SetValue(this, newAccumRateStat());
                else if (t == typeof(AccumRateStat[]))
                    fi.SetValue(this, newAccumRateStatArray(Config.N));
                else if (t == typeof(SampleAvgStat))
                    fi.SetValue(this, newSampleAvgStat());
                else if (t == typeof(SampleAvgStat[]))
                    fi.SetValue(this, newSampleAvgStatArray(Config.N));
                else if (t == typeof(SignedSampleAvgStat))
                    fi.SetValue(this, newSignedSampleAvgStat());
                else if (t == typeof(SignedSampleAvgStat[]))
                    fi.SetValue(this, newSignedSampleAvgStatArray(Config.N));
                else if (t == typeof(SamplePercentAvgStat))
                    fi.SetValue(this, newSamplePercentAvgStat());
                else if (t == typeof(SamplePercentAvgStat[]))
                    fi.SetValue(this, newSamplePercentAvgStatArray(Config.N));
                else if (t == typeof(PerQuantumStat))
                    fi.SetValue(this, newPerQuantumStat());
                else if (t == typeof(PerQuantumStat[]))
                    fi.SetValue(this, newPerQuantumStatArray(Config.N));
                else if (t == typeof(DictSampleStat))
                    fi.SetValue(this, newDictSampleStat());
                else if (t == typeof(DictSampleStat[]))
                    fi.SetValue(this, newDictSampleStatArray(Config.N));
            }
        }

        protected AccumStat newAccumStat()
        {
            return new AccumStat();
        }

        protected AccumStat[] newAccumStatArray(int N)
        {
            AccumStat[] ret = new AccumStat[N];
            for (int i = 0; i < N; i++)
                ret[i] = new AccumStat();
            return ret;
        }

        protected AccumStat[,] newAccumStatArray2D(int N, int M)
        {
            AccumStat[,] ret = new AccumStat[N, M];
            for (int i = 0; i < N; i++)
                for (int j = 0; j < M; j++)
                    ret[i, j] = new AccumStat();
            return ret;
        }

        protected AccumRateStat newAccumRateStat()
        {
            return new AccumRateStat();
        }

        protected AccumRateStat[] newAccumRateStatArray(int N)
        {
            AccumRateStat[] ret = new AccumRateStat[N];
            for (int i = 0; i < N; i++)
                ret[i] = new AccumRateStat();
            return ret;
        }

        protected SampleAvgStat newSampleAvgStat()
        {
            return new SampleAvgStat();
        }

        protected SampleAvgStat[] newSampleAvgStatArray(int N)
        {
            SampleAvgStat[] ret = new SampleAvgStat[N];
            for (int i = 0; i < N; i++)
                ret[i] = new SampleAvgStat();
            return ret;
        }

        protected SignedSampleAvgStat newSignedSampleAvgStat()
        {
            return new SignedSampleAvgStat();
        }

        protected SignedSampleAvgStat[] newSignedSampleAvgStatArray(int N)
        {
            SignedSampleAvgStat[] ret = new SignedSampleAvgStat[N];
            for (int i = 0; i < N; i++)
                ret[i] = new SignedSampleAvgStat();
            return ret;
        }

        protected SamplePercentAvgStat newSamplePercentAvgStat()
        {
            return new SamplePercentAvgStat();
        }

        protected SamplePercentAvgStat[] newSamplePercentAvgStatArray(int N)
        {
            SamplePercentAvgStat[] ret = new SamplePercentAvgStat[N];
            for (int i = 0; i < N; i++)
                ret[i] = new SamplePercentAvgStat();
            return ret;
        }

        protected PerQuantumStat newPerQuantumStat()
        {
            return new PerQuantumStat();
        }

        protected PerQuantumStat[] newPerQuantumStatArray(int N)
        {
            PerQuantumStat[] ret = new PerQuantumStat[N];
            for (int i = 0; i < N; i++)
                ret[i] = newPerQuantumStat();
            return ret;
        }

        protected DictSampleStat newDictSampleStat()
        {
            return new DictSampleStat();
        }

        protected DictSampleStat[] newDictSampleStatArray(int N)
        {
            DictSampleStat[] ret = new DictSampleStat[N];
            for (int i = 0; i < N; i++)
                ret[i] = newDictSampleStat();
            return ret;
        }
    }

    public class Stat : StatGroup
    {
        public static ProcStat[] procs = new ProcStat[Config.N];
        public static MemCtrlStat[] mctrls = new MemCtrlStat[Config.mem.chan_max];
        public static BusStat[] busses = new BusStat[Config.mem.chan_max];
        public static BankStat[, ,] banks = new BankStat[Config.mem.chan_max, Config.mem.rank_max, Config.mem.bank_max];
        public static MemSchedStat[] scheds = new MemSchedStat[Config.mem.chan_max];
        public static CacheStat[] caches;

        static Stat()
        {
            for (uint i = 0; i < procs.Length; i++)
                procs[i] = new ProcStat();
            for (uint i = 0; i < mctrls.Length; i++)
                mctrls[i] = new MemCtrlStat(i);
            for (uint i = 0; i < busses.Length; i++)
                busses[i] = new BusStat(i);

            for (uint c = 0; c < Config.mem.chan_max; c++)
            {
                for (uint r = 0; r < Config.mem.rank_max; r++)
                {
                    for (uint b = 0; b < Config.mem.bank_max; b++)
                    {
                        banks[c, r, b] = new BankStat(c, r, b);
                    }
                }
            }

            // Shared cache
            if (Config.proc.shared_l2)
                caches = new CacheStat[1];
            else
                caches = new CacheStat[Config.N];
            for (int i = 0; i < caches.Length; i++)
                caches[i] = new CacheStat();
        }

        public void Report(TextWriter tw)
        {
            tw.WriteLine("{");

            tw.WriteLine("\"proc\":");
            tw.WriteLine("[");
            for (int i = 0; i < procs.Length; i++)
            {
                procs[i].DumpJSON(tw);
                if (i != procs.Length - 1)
                    tw.WriteLine(",");
            }
            tw.WriteLine("],");

            tw.WriteLine("\"cache\":");
            tw.WriteLine("[");
            for (int i = 0; i < caches.Length; i++)
            {
                caches[i].DumpJSON(tw);
                if (i != caches.Length - 1)
                    tw.WriteLine(",");
            }
            tw.WriteLine("],");

            tw.WriteLine("\"mctrl\":");
            tw.WriteLine("[");
            for (int i = 0; i < mctrls.Length; i++)
            {
                mctrls[i].DumpJSON(tw);
                if (i != mctrls.Length - 1)
                    tw.WriteLine(",");
            }
            tw.WriteLine("],");

            tw.WriteLine("\"bus\":");
            tw.WriteLine("[");
            for (int i = 0; i < busses.Length; i++)
            {
                busses[i].DumpJSON(tw);
                if (i != busses.Length - 1)
                    tw.WriteLine(",");
            }
            tw.WriteLine("],");

            tw.WriteLine("\"bank\":");
            tw.WriteLine("[");
            for (int c = 0; c < Config.mem.chan_max; c++)
            {
                for (int r = 0; r < Config.mem.rank_max; r++)
                {
                    for (int b = 0; b < Config.mem.bank_max; b++)
                    {
                        banks[c, r, b].DumpJSON(tw);
                        if (!((c == Config.mem.chan_max - 1) && (r == Config.mem.rank_max - 1) && (b == Config.mem.bank_max - 1)))
                        {
                            tw.WriteLine(",");
                        }
                    }
                }
            }
            tw.WriteLine("],");

            // Config
            tw.WriteLine("\"ProcConfig\":");
            tw.WriteLine("[");
            Config.proc.DumpJSON(tw);
            tw.WriteLine("],");

            tw.WriteLine("\"MemConfig\":");
            tw.WriteLine("[");
            Config.mem.DumpJSON(tw);
            tw.WriteLine("],");

            tw.WriteLine("\"MemCtrlConfig\":");
            tw.WriteLine("[");
            Config.mctrl.DumpJSON(tw);
            tw.WriteLine("],");

            tw.WriteLine("\"MemSchedConfig\":");
            tw.WriteLine("[");
            Config.sched.DumpJSON(tw);
            tw.WriteLine("]");

            tw.WriteLine("}");
        }
    }

    public class StatObject
    {
        protected bool is_collect = true;
        protected ulong endtime;

        public virtual void Finish(ulong time)
        {
            endtime = time;
            is_collect = false;
        }

        public virtual void Reset()
        {
            throw new Exception();
        }

        public virtual void DumpJSON(TextWriter tw)
        {
            throw new Exception();
        }
    }

    public class AccumStat : StatObject
    {
        protected ulong count;

        public void collect()
        {
            if (!is_collect) return;
            count++;
        }

        public void collect(int inc)
        {
            if (!is_collect) return;
            count += (ulong)inc;
        }

        public void collect(uint inc)
        {
            if (!is_collect) return;
            count += (ulong)inc;
        }

        public ulong Count
        {
            get { return count; }
        }

        public override void Reset()
        {
            count = 0;
            endtime = 0;
        }

        public override void DumpJSON(TextWriter tw)
        {
            tw.Write("{0}", count);
        }
    }

    public class AccumRateStat : AccumStat
    {
        private double rate;

        public override void Finish(ulong time)
        {
            base.Finish(time);
            rate = ((double)count) / time;
        }

        public override void DumpJSON(TextWriter tw)
        {
            tw.Write("{0:0.###e0}", rate);
        }
    }

    public class SampleAvgStat : StatObject
    {
        protected ulong sample;
        protected ulong count;
        protected double avg;

        public void Collect()
        {
            if (!is_collect) return;
            count++;
            sample++;
        }

        public void collect(int inc)
        {
            if (!is_collect) return;
            count += (ulong)inc;
            sample++;
        }

        public void collect_count(int inc)
        {
            if (!is_collect) return;
            count += (ulong)inc;
        }

        public void set_count(int inc)
        {
            if (!is_collect) return;
            count = (ulong)inc;
        }

        public void record_max(int data)
        {
            if (sample == 0)
            {
                sample = 1;
                count = (ulong)data;
            }
            else if ((ulong)data >= count)
                count = (ulong)data;
        }

        public void record_min(int data)
        {
            if (sample == 0)
            {
                sample = 1;
                count = (ulong)data;
            }
            else if ((ulong)data < count)
                count = (ulong)data;
        }

        public void Collect(uint inc)
        {
            if (!is_collect) return;
            count += (ulong)inc;
            sample++;
        }

        public void Collect(ulong inc)
        {
            if (!is_collect) return;
            count += inc;
            sample++;
        }

        public ulong Count
        {
            get { return count; }
        }

        public ulong Sample
        {
            get { return sample; }
        }

        public Double Average
        {
            get { return ((double)count) / sample; }
        }

        public override void Reset()
        {
            count = 0;
            sample = 0;
            endtime = 0;
        }

        public override void Finish(ulong time)
        {
            base.Finish(time);
            if (sample == 0) avg = 0;
            else avg = ((double)count) / sample;
        }

        public override void DumpJSON(TextWriter tw)
        {
            tw.Write("{0:0.###e0}", avg);
        }
    }

    public class SignedSampleAvgStat : StatObject
    {
        protected long sample;
        protected long count;
        protected double avg;

        public void Collect()
        {
            if (!is_collect) return;
            count++;
            sample++;
        }

        public void collect(int inc)
        {
            if (!is_collect) return;
            count += inc;
            sample++;
        }

        public void Collect(int inc)
        {
            if (!is_collect) return;
            count += inc;
            sample++;
        }

        public void Collect(long inc)
        {
            if (!is_collect) return;
            count += inc;
            sample++;
        }

        public long Count
        {
            get { return count; }
        }

        public Double Average
        {
            get { return ((double)count) / sample; }
        }

        public override void Reset()
        {
            count = 0;
            sample = 0;
            endtime = 0;
        }

        public override void Finish(ulong time)
        {
            base.Finish(time);
            if (sample == 0) avg = 0;
            else avg = ((double)count) / sample;
        }

        public override void DumpJSON(TextWriter tw)
        {
            tw.Write("{0:0.###e0}", avg);
        }
    }

    public class SamplePercentAvgStat : SampleAvgStat
    {
        public override void DumpJSON(TextWriter tw)
        {
            tw.Write("{0:##0.00}", 100 * avg);
        }
    }

    public class PerQuantumStat : StatObject
    {
        protected List<float> history = new List<float>();

        public void EndQuantum(float val)
        {
            history.Add(val);
        }

        public override void Reset()
        {
            history.Clear();
        }

        public override void DumpJSON(TextWriter tw)
        {
            tw.Write("[");
            bool first = true;
            foreach (float i in history)
            {
                if (!first) tw.Write(",");
                else first = false;
                tw.Write("{0:0.###e0}", i);
            }
            tw.Write("]");
        }
    }

    public class DictSampleStat : StatObject
    {
        private Dictionary<ulong, double> d;

        public DictSampleStat()
        {
            d = new Dictionary<ulong, double>();
        }

        public void Collect(ulong key)
        {
            if (!is_collect) return;

            if (d.ContainsKey(key))
                d[key]++;
            else
                d[key] = 1;
        }

        public override void Reset()
        {
            d.Clear();
        }

        public override void DumpJSON(TextWriter tw)
        {
            tw.Write("{");
            bool first = true;
            foreach (KeyValuePair<ulong, double> pair in d)
            {
                if (first) first = false;
                else tw.Write(",");

                tw.Write("\"{0}\":{1}", pair.Key, pair.Value);
            }
            tw.Write("}");
        }
    }
}