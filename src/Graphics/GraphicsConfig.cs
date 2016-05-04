using Ramulator.Sim;

namespace Ramulator.Graphics
{
    public class GraphicsConfig : ConfigGroup
    {
        public bool gui_enabled = false;

        // Which channel's commands to draw
        public int gui_chan_num = 0;

        // Which ranks' commands to draw
        public int gui_rank_num = 0;

        public int x_line_start_pos = 30;
        public int y_line_start_pos = 10;
        public int gui_font_size = 8;
        public int gui_box_width_for_string = 75;

        // How many cycles each pixel represents
        public double gui_cycle_per_pixel = 0.5;

        public long gui_draw_start_cycle = -1;
        public long gui_draw_end_cycle = -1;

        protected override bool set_special_param(string param, string val)
        {
            return false;
        }

        public override void finalize()
        {
        }
    }
}