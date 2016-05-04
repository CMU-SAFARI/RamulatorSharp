using System;
using System.Drawing;
using Ramulator.MemCtrl;
using Ramulator.Sim;

namespace Ramulator.Graphics
{
    public class TimeLineGraphics
    {
        // Start and end point of each timeline
        private readonly BankTimeLineState[] _bankStates = new BankTimeLineState[Config.mem.bank_max];

        public TimeLineGraphics()
        {
            for (var b = 0; b < Config.mem.bank_max; b++)
                _bankStates[b] = new BankTimeLineState();
        }

        public void UpdateBankState(Point start, Point end, Point text, int bid)
        {
            _bankStates[bid].UpdateState(start, end, text);
        }

        public void DrawCmd(Cmd cmd, long cycles)
        {
            if (OkToDraw(cycles, (int) cmd.Addr.cid, (int) cmd.Addr.rid))
                _bankStates[cmd.Addr.bid].DrawCmd(cmd, cycles);
        }

        public void DrawBox(int cid, int rid, int bid, int width, long cycles, Brush boxColor)
        {
            if (OkToDraw(cycles, cid, rid))
                _bankStates[bid].DrawBox(width, cycles, boxColor);
        }

        public bool OkToDraw(long cycles, int cid, int rid)
        {
            // Restrict to the specified channel and rank
            if (cid != Config.gfx.gui_chan_num || rid != Config.gfx.gui_rank_num)
                return false;
            return (Config.gfx.gui_draw_start_cycle == -1 && Config.gfx.gui_draw_end_cycle == -1) ||
                   (cycles >= Config.gfx.gui_draw_start_cycle && cycles <= Config.gfx.gui_draw_end_cycle);
        }
    }

    public class BankTimeLineState
    {
        // Current drawing position
        private Point _curPosition;

        public void UpdateState(Point start, Point end, Point text)
        {
            _curPosition = text;
        }

        public void DrawCmd(Cmd cmd, long cycles)
        {
            _curPosition.X = Config.gfx.x_line_start_pos +
                            (int) Math.Ceiling((cycles - Config.gfx.gui_draw_start_cycle)/Config.gfx.gui_cycle_per_pixel);

            var formGraphics = Program.gui.CreateGraphics();
            // Report SA numbers
            var drawString = cmd.Type + " sid:" + cmd.Addr.said + "\n" + cycles;
            if (cmd.Type == CmdType.SEL_SA)
                drawString = "SSA sid:" + cmd.Addr.said + "\n" + cycles;

            var drawFont = new Font("Arial", Config.gfx.gui_font_size);

            // Meausre string width
            var stringSize = formGraphics.MeasureString(drawString, drawFont, Config.gfx.gui_box_width_for_string);

            // Draw each command on the timeline
            switch (cmd.Type)
            {
                case CmdType.REF_BANK:
                case CmdType.REF_RANK:
                    // Width of the filled rectangle
                    var width = cmd.Type == CmdType.REF_BANK ? (int) Sim.Sim.Mctrls[0].tc.tRFCpb : (int) Sim.Sim.Mctrls[0].tc.tRFC;
                    width = (int) (width/Config.gfx.gui_cycle_per_pixel);
                    Program.gui.addTextBox(drawString, _curPosition.X, _curPosition.Y,
                        width, (int) stringSize.Height, Brushes.Bisque, Brushes.Black);
                    break;
                case CmdType.SEL_SA:
                    Program.gui.addTextBox(drawString, _curPosition.X, _curPosition.Y,
                        (int) stringSize.Width, (int) stringSize.Height, Brushes.Bisque, Brushes.Black);
                    break;
                default:
                    Program.gui.addTextBox(drawString, _curPosition.X, _curPosition.Y,
                        (int) stringSize.Width, (int) stringSize.Height, Brushes.White, Brushes.Black);
                    break;
            }

            // Clean up
            drawFont.Dispose();
            formGraphics.Dispose();
        }

        public void DrawBox(int width, long cycles, Brush boxColor)
        {
            if (cycles < Config.gfx.gui_draw_start_cycle)
                cycles = Config.gfx.gui_draw_start_cycle;

            _curPosition.X = Config.gfx.x_line_start_pos +
                            (int) Math.Ceiling((cycles - Config.gfx.gui_draw_start_cycle)/Config.gfx.gui_cycle_per_pixel);
            var formGraphics = Program.gui.CreateGraphics();
            const string drawString = "test\n";
            var drawFont = new Font("Arial", Config.gfx.gui_font_size);
            var stringSize = formGraphics.MeasureString(drawString, drawFont, Config.gfx.gui_box_width_for_string);
            Program.gui.addTextBox("", _curPosition.X, _curPosition.Y,
                width, (int) stringSize.Height, boxColor, Brushes.Black);
        }
    }
}