using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace SSCP.ShellPower
{
    public partial class MPPT_ParamForm : Form
    {
        private MPPTSpec spec;
        private double vmaxIn, vmaxOut, imaxIn, imaxOut, vmin, imin;
        private double vdrop, maxBR, constEffOffest, boostEffImpact;


        public MPPT_ParamForm(MPPTSpec spec)
        {
            this.spec = spec;
            InitializeComponent();
            ResetTextBoxes();
        }
        private void ResetTextBoxes()
        {
            textBoxVmaxIn.Text = "" + spec.VmaxIn;
            textBoxVmaxOut.Text = "" + spec.VmaxOut;
            textBoxImaxIn.Text = "" + spec.ImaxIn;
            textBoxImaxOut.Text = "" + spec.ImaxOut;
            textBoxVdrop.Text = "" + spec.Vdrop;
            textBoxVmin.Text = "" + spec.Vmin;
            textBoxImin.Text = "" + spec.Imin;
            textBoxMaxBR.Text = "" + spec.MaxBR;


            textBoxConsEff.Text = "" + spec.ConstEffOffset;
            textBoxBReff.Text = "" + spec.BoostRatioEffImpact;
        }

        private bool ValidateEntries()
        {
            bool valid = true;
            valid &= ViewUtil.ValidateEntry(textBoxVmaxIn, out vmaxIn, vmin, 1000);
            valid &= ViewUtil.ValidateEntry(textBoxVmaxOut, out vmaxOut, vmin, 1000);
            valid &= ViewUtil.ValidateEntry(textBoxImaxIn, out imaxIn, imin, 50);
            valid &= ViewUtil.ValidateEntry(textBoxImaxOut, out imaxOut, imin, 50);
            valid &= ViewUtil.ValidateEntry(textBoxVdrop, out vdrop, 0, 10);

            valid &= ViewUtil.ValidateEntry(textBoxVmin, out vmin, 0, 100);
            valid &= ViewUtil.ValidateEntry(textBoxImin, out imin, 0, 50);
            valid &= ViewUtil.ValidateEntry(textBoxMaxBR, out maxBR, 1, 50);

            valid &= ViewUtil.ValidateEntry(textBoxConsEff, out constEffOffest, 0, 1);
            valid &= ViewUtil.ValidateEntry(textBoxBReff, out boostEffImpact, 0, 1);
            return valid;
        }

        private void buttonOK_Click(object sender, EventArgs e)
        {
            if (ValidateEntries())
            {
                UpdateSpec(spec);
                Close();
            }
            else
            {
                MessageBox.Show("Some of those entries don't look right. Try again.");
            }

        }
        private void UpdateSpec(MPPTSpec spec)
        {
            spec.VmaxIn = vmaxIn;
            spec.VmaxOut = vmaxOut;
            spec.ImaxIn = imaxIn;
            spec.ImaxOut = imaxOut;
            spec.Vdrop = vdrop;
            spec.Vmin = vmin;
            spec.Imin = imin;
            spec.MaxBR = maxBR;
            spec.ConstEffOffset = constEffOffest;
            spec.BoostRatioEffImpact = boostEffImpact;
        }

        private void buttonCancel_Click(object sender, EventArgs e)
        {
            Close();
        }
    }
}
