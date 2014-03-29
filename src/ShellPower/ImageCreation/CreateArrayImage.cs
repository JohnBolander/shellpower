﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Diagnostics;
using System.Windows.Forms;

namespace SSCP.ShellPower
{
    public class CreateArrayImage
    {
        private static double radius = 80;
        private static double squareDim = 125;
        private static float SCALE = 2;
        private static float DIAMETER = (float)radius*2*SCALE;
        private static float CUT = (float)squareDim * SCALE;
        private static float spaceing = 4 * SCALE;
        private static float xEdge = 70 * SCALE;
        private static float yEdge = 100 * SCALE;
        private static int m = 13; //cannot exceed 255
        private static int n = 31; //cannot exceed 255
        private static int sizex = (int) Math.Round(xEdge * 2 + CUT * m + spaceing * (m - 1));
        private static int sizey = (int) Math.Round(yEdge * 2 + CUT * n + spaceing * (n - 1));

        private static double heightTri = squareDim/2;
        private static double circleArc = 2*Math.PI;
        private static double percentCirc = 2*Math.Acos(heightTri/radius)/circleArc;
        private static double baseTri = 2 * Math.Sqrt(radius * radius - heightTri * heightTri);
        private static double areaTri = .5*heightTri*baseTri;
        private static double croppedArea = Math.PI * radius * radius * percentCirc - areaTri;
        private static double areaCell = Math.PI * radius * radius - 4 * croppedArea;
        private static double maxArraySize = 6;
        private static double maxAllowedCells = maxArraySize / (areaCell * .000001);
        private string message = String.Format("Cell Area is {0}, allowing {1} cells", areaCell, maxAllowedCells);
        //Debug.WriteLine("Cell Area is {0}, allowing {1} cells", areaCell, maxAllowedCells);
        //DialogResult result = MessageBox.Show(message, "Possible Noncergence", MessageBoxButtons.YesNo);


        public static void CreateImage(){
            using (Bitmap b = new Bitmap(sizex, sizey))
            {
                using (Graphics g = Graphics.FromImage(b)){
                    g.Clear(Color.LightGray);
                    DrawArray(g);
                }
                b.Save(@"../../../../arrays/generic/newfile.png");
            }
            Debug.WriteLine("percentArc: {0} Cropped Area: {1}, base triange: {2}", percentCirc, croppedArea, baseTri);
            Debug.WriteLine("Cell Area is {0}, allowing {1} cells", areaCell, maxAllowedCells);
        }
        private static void DrawCell(Graphics g, float x, float y, Color color)
        {
            float circX = x - (DIAMETER - CUT) / 2;
            float circY = y - (DIAMETER - CUT) / 2; 
            SolidBrush sb = new SolidBrush(color);
            RectangleF rectClip = new RectangleF();
            rectClip.Height = CUT;
            rectClip.Width = CUT;
            rectClip.X = x;
            rectClip.Y = y;
            g.Clip = new Region(rectClip);
            g.FillEllipse(sb, circX, circY, DIAMETER, DIAMETER);
        }
        private static void DrawArray(Graphics g)
        {
            float step = CUT + spaceing;
            int redInc = 255 / m;
            int greenInc = 255/n;
            int blue = 255;
            int red = 0;
            for (float x = xEdge; x < m * step; x += step){
                int green = 0;
                for (float y = yEdge; y < n * step; y += step){
                    Color color = Color.FromArgb(red, green, blue);
                    DrawCell(g, x, y, color);
                    green += greenInc;
                }
                red += redInc;
            }
        }

    }
}


//g.fill
//SolidBrush newbrush = new SolidBrush(Color.LightGray);
//Rectangle[] rects = new Rectangle[4];
//Rectangle rect1 = new Rectangle();
//rect1.Height = 35;
//rect1.Width = 320;
//rect1.X = 0;
//rect1.Y = 0;
//rects[0] = rect1;

//Rectangle rect2 = new Rectangle();
//rect2.Height = 320;
//rect2.Width = 35;
//rect2.X = 0;
//rect2.Y = 0;
//rects[1] = rect2;
//Rectangle rect3 = new Rectangle();
//rect3.Height = 320;
//rect3.Width = 35;
//rect3.X = 285;
//rect3.Y = 0;
//rects[2] = rect3;
//Rectangle rect4 = new Rectangle();
//rect4.Height = 35;
//rect4.Width = 320;
//rect4.X = 0;
//rect4.Y = 285;
//rects[3] = rect4;

//g.FillRectangles(newbrush, rects);