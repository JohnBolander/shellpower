using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SSCP.ShellPower{
    public class MPPTSpec
    {

        /// <summary>
        /// Maximum allowed input voltage of MPPT
        /// </summary>
        public double VmaxIn { get; set; }
        /// <summary>
        /// Maximum allowed output voltage of MPPT
        /// </summary>
        public double VmaxOut { get; set; }
        /// <summary>
        /// Minimum voltage that the MPPT will function at the input/output
        /// </summary>
        public double Vmin { get; set; }
        /// <summary>
        /// Maximum allowed input current of MPPT
        /// </summary>
        public double ImaxIn { get; set; }
        /// <summary>
        /// Maximum allowed output current of MPPT
        /// </summary>
        public double ImaxOut { get; set; }
        /// <summary>
        /// Minimum current that the MPPT will function at the input/output
        /// </summary>
        public double Imin { get; set; }
        /// <summary>
        /// The intrinsic voltage drop from input to output
        /// The output must be this much higher than the input
        /// </summary>
        public double Vdrop { get; set; }
        /// <summary>
        /// The Maximum Boost ratio of the MPPT
        /// </summary>
        public double MaxBR { get; set; }
        /// <summary>
        /// Is the constant term for efficicny (the efficiency when the boost ratio is just above 1)
        /// </summary>
        public double ConstEffOffset { get; set; }
        /// <summary>
        /// The impact boost ratio has on efficiency
        /// </summary>
        public double BoostRatioEffImpact { get; set; }

        /// <summary>
        /// Calculates the Efficiency of the MPPT based on Boost Ratio and a max efficiency
        /// </summary>
        public double Efficiency(double vin, double vout)
        {
            return ConstEffOffset - (vout / vin * BoostRatioEffImpact);
        }

        public double PowerOut(double powerIn, double vin, double vout)
        {
            return Efficiency(vin, vout) * powerIn;
        }

        public double CurrentOut(double powerIn, double vin, double vout)
        {
            return PowerOut(powerIn, vin, vout) / vout;
        }
    }
}
