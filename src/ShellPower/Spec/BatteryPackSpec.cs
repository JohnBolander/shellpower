using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SSCP.ShellPower
{
    public class BatterPackSpec
    {
        /// <summary>
        /// Maximum battery pack votage
        /// </summary>
        public double Vmax { get; set; }
        /// <summary>
        /// Minimum allowed battery pack voltage
        /// </summary>
        public double Vmin { get; set; }
        /// <summary>
        /// Nominal battey pack voltage
        /// </summary>
        public double Vnom { get; set; }
    }
}
