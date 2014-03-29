using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Drawing;

namespace SSCP.ShellPower
{
    public class ArraySimDefaults
    {
        public static ArraySimulationStepInput CreateDefaultInput()
        {
            ArraySimulationStepInput input = new ArraySimulationStepInput();
            input.Array = CreateDefaultArraySpec();
            input.MPPT = CreateDefaultMPPTSpec();
            InitTimeAndPlace(input);
            InitializeConditions(input);
            return input;
        }

        private static void InitTimeAndPlace(ArraySimulationStepInput simInput)
        {
            // Coober Pedy, SA, heading due south
            simInput.Longitude = 134.75555;
            simInput.Latitude = -29.01111;
            simInput.Heading = Math.PI;

            // Start of WSC 2013
            simInput.Utc = new DateTime(2013, 10, 6, 8, 0, 0).AddHours(-9.5);
            simInput.Timezone = TimeZoneInfo.FindSystemTimeZoneById("AUS Central Standard Time");
        }

        /// <summary>
        /// Hack to make debugging faster.
        /// </summary>
        private static ArraySpec CreateDefaultArraySpec()
        {
            ArraySpec array = new ArraySpec();
            array.LayoutBoundsXZ = new RectangleF(-0.115f, -0.23f, 2.15f, 4.820f);
            array.LayoutTexture = ArrayModelControl.DEFAULT_TEX;

            // Sunpower C60 Bin I
            // http://www.kyletsai.com/uploads/9/7/5/3/9753015/sunpower_c60_bin_ghi.pdf
            CellSpec cellSpec = array.CellSpec;
            cellSpec.IscStc = 6.27;
            cellSpec.VocStc = 0.686;
            cellSpec.DIscDT = -0.0020; // approx, computed
            cellSpec.DVocDT = -0.0018;
            cellSpec.Area = 0.015555; // m^2
            cellSpec.NIdeal = 1.26; // fudge
            cellSpec.SeriesR = 0.003; // ohms

            // Average bypass diode
            DiodeSpec diodeSpec = array.BypassDiodeSpec;
            diodeSpec.VoltageDrop = 0.35;

            return array;
        }

        private static void InitializeConditions(ArraySimulationStepInput simInput)
        {
            simInput.Temperature = 25; // STC, 25 Celcius
            simInput.Irradiance = 1050; // not STC
            simInput.IndirectIrradiance = 70; // not STC
            simInput.EncapuslationLoss = 0.025; // 2.5 %
        }
        private static MPPTSpec CreateDefaultMPPTSpec()
        {
            //the following values are for the Dilithium Power Systems  Photon MPPt
            MPPTSpec mpptSpec = new MPPTSpec();
            mpptSpec.VmaxIn = 159;
            mpptSpec.VmaxOut = 160;
            mpptSpec.ImaxOut = 12;
            mpptSpec.ImaxIn = 12;
            mpptSpec.Vdrop = 1;
            mpptSpec.Vmin = 5;
            mpptSpec.Imin = .75;
            mpptSpec.MaxBR = 16;
            mpptSpec.ConstEffOffset = .9948;
            mpptSpec.BoostRatioEffImpact = .0052;
            return mpptSpec;
        }
    }
}
