using Ramulator.Graphics;
using System;
using System.Drawing;
using System.Windows.Forms;
using Ramulator.Sim;

namespace Ramulator
{
    public partial class Form1 : Form
    {
        // Private vars
        private Sim.Sim sim;

        // Public vars
        public TimeLineGraphics tgfx;

        public Form1(Sim.Sim sim)
        {
            InitializeComponent();
            this.sim = sim;
            tgfx = new TimeLineGraphics();
        }

        private void Form1_Load(object sender, EventArgs e) { }

        private bool parseCaptureTime()
        {
            long s, e;

            if (long.TryParse(textBox1.Text, out s) && long.TryParse(textBox2.Text, out e))
            {
                Config.gfx.gui_draw_start_cycle = s;
                Config.gfx.gui_draw_end_cycle = e;

                if (s > e)
                {
                    MessageBox.Show("Invalide start and end cycles");
                    return false;
                }
            }
            return true;
        }

        private void button1_Click_1(object sender, EventArgs e)
        {
            if (!parseCaptureTime()) 
                return;
            MessageBox.Show("Run Simulator!\n");
            // Disable the button
            button1.Enabled = false;
            // Draw the lines
            drawBankLines(tgfx);
            // Run sim
            sim.RunAll();
            MessageBox.Show("Finish Running Simulator! Click X to exit\n");
        }

        // Draw a command timeline for each bank -- The lines drawing get erased when the form gets expanded
        private void drawBankLines(TimeLineGraphics tgfx)
        {
            System.Drawing.Pen myPen;
            myPen = new Pen(Color.Black);
            System.Drawing.Graphics formGraphics = this.CreateGraphics();

            Font drawFont = new System.Drawing.Font("Arial", Config.gfx.gui_font_size);

            // Draw a line for each bank
            uint numBanks = Config.mem.bank_max;
            uint yStart = (uint)Config.gfx.y_line_start_pos;
            string placeHolder = "CMD\nCYC\nROW";
            SizeF stringSize = formGraphics.MeasureString(placeHolder, drawFont, Config.gfx.gui_box_width_for_string);
            // Determine the spacing between each command timeline
            uint lineSpace = (uint)(button1.Location.Y - yStart - stringSize.Height -
                myPen.Width * numBanks) / (numBanks - 1);

            // One line at a time
            for (int b = 0; b < numBanks; b++)
            {
                int yCoord = (int)(yStart + b * lineSpace);

                // Bank id string
                Point text = new Point(0, yCoord);
                string bstr = "B" + b;
                //formGraphics.DrawString(bstr, drawFont, Brushes.Black, text);
                stringSize = formGraphics.MeasureString(bstr, drawFont, Config.gfx.gui_box_width_for_string);
                addTextBox(bstr, text.X, text.Y, (int)stringSize.Width, (int)stringSize.Height,
                    Brushes.Crimson, Brushes.White);

                // Bank cmd timelines
                int lineYCoord = yCoord + (int)stringSize.Height + 1;
                Point start = new Point(Config.gfx.x_line_start_pos, lineYCoord);
                Point end = new Point(this.Size.Width - 50, lineYCoord);
                formGraphics.DrawLine(myPen, start, end);
                text.X = start.X;
                tgfx.UpdateBankState(start, end, text, b);
            }
        }

        // Add a text box to the form
        public void addTextBox(string str, int left, int top, int width, int height,
            Brush fillRectBrush, Brush textBrush)
        {
            MyBox box = new MyBox();
            box.Top = top;
            box.Left = left;
            box.Width = width;
            box.Height = height;
            box.Text = str;
            box.fillRectBrush = fillRectBrush;
            box.textBrush = textBrush;
            this.Controls.Add(box);
            box.BringToFront();
        }

        // Small rectangles that act as controls so that the form can autoscroll
        // when controls are added along the horizontal axis
        public class MyBox : Control
        {
            Font drawFont;

            public Brush fillRectBrush;
            public Brush textBrush;

            public MyBox()
            {
                this.Width = 100;
                this.Height = 100;
                drawFont = new System.Drawing.Font("Arial", Config.gfx.gui_font_size);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                if (fillRectBrush != Brushes.White)
                {
                    Rectangle rec = new Rectangle(0, 0, this.Width, this.Height);
                    e.Graphics.FillRectangle(fillRectBrush, rec);
                }
                e.Graphics.DrawString(this.Text, drawFont, textBrush, 0, 0);
            }
        }

        private void textBox1_TextChanged(object sender, EventArgs e) { }
        private void textBox2_TextChanged(object sender, EventArgs e) { }
    }
}