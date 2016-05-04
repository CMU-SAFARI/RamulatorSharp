using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Ramulator.Sim;

namespace Ramulator
{
    internal class Program
    {
        // Graphics for drawing
        public static Form1 gui = null;

        public static TextWriter debug_cmd_dump_file = null;

        [STAThread]
        private static void Main(string[] args)
        {
            //*IMPORTANT* without a trace listener, mono can't output Dbg.Assert() */
            Debug.Listeners.Add(new ConsoleTraceListener());
            Config config = new Config();
            config.read(args);

            if (Config.debug_file_name != "")
                debug_cmd_dump_file = new StreamWriter(Config.debug_file_name);

            // Main simulator module
            Sim.Sim sim = new Sim.Sim();
            sim.Initialize();
            Thread.CurrentThread.Priority = ThreadPriority.Normal;

            // Run the form
            if (Config.gfx.gui_enabled)
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                gui = new Form1(sim);
                Application.Run(gui);
            }
            else
                sim.RunAll();

            if (debug_cmd_dump_file != null)
                debug_cmd_dump_file.Close();
            // Clean up the simulator and dump stats
            sim.Finish();
            TextWriter tw = new StreamWriter(Config.output);
            sim.Stat.Report(tw);
            tw.Close();
        }
    }
}