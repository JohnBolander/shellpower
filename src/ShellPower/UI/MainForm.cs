﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Xml;
using System.Threading;
using OpenTK;
using System.IO;

namespace SSCP.ShellPower {
    public partial class MainForm : Form {
        /* model */
        ArraySimulationStepInput simInput;// = new ArraySimulationStepInput();
        Shadow shadow;

        /* sub views */
        ArrayLayoutForm arrayLayoutForm;
        CellParamsForm cellParamsForm;
        ArrayDimensionsForm arrayDimsForm;

        /* simulator */
        ArraySimulator simulator;

        /* optimizer */
        ArrayBuilder builder;

        public MainForm() {
            // init view
            InitializeComponent();
            tabControl1.SelectedIndex = 0;
            timeStepBox.Text = "10";
            labelArrPower.Rtf = @"{\rtf1\ansi\deff0 Load model, load texture, then click simulate. }";

            // init model
            simInput = ArraySimDefaults.CreateDefaultInput();
            simInput.Array.Mesh = MeshIO.LoadMesh("../../../../arrays/luminos/luminos.stl");
            LoadModel(simInput.Array.Mesh, "luminos.stl");
            LoadLayoutDefault("../../../../arrays/luminos/luminos-splines-6-string-no-bypass.png");
            CalculateSimStepGui();

            CreateArrayImage.CreateImage();

            // init subviews
            arrayLayoutForm = new ArrayLayoutForm(simInput.Array);
            cellParamsForm = new CellParamsForm(simInput);
            glControl.Array = simInput.Array;
            simInputControls.SimInput = simInput;
            InitOutputView();
        }

        private void InitOutputView() {
            outputArrayLayoutControl.Editable = false;
            outputArrayLayoutControl.Array = simInput.Array;
        }

        private void InitSimulator()
        {
            if (simulator == null)
            {
                simulator = new ArraySimulator();
            }
        }

        private void InitBuilder(ArraySpec array, double cTemp, MPPTSpec mpptSpec, BatterPackSpec packSpec)
        {
            if (builder == null)
            {
                builder = new ArrayBuilder(array, cTemp, mpptSpec, packSpec);
            }
        }
        private void LoadLayoutDefault(string filename){
            //var filname  = openFileDialogArray.FileName;
            Bitmap bitmap = new Bitmap(filename);
            if (bitmap == null)
            {
                return;
            }

            // apply the new texture, rollback if it fails
            Bitmap origTexture = simInput.Array.LayoutTexture;
            try
            {
                simInput.Array.LayoutTexture = bitmap;
                simInput.Array.ReadStringsFromColors();
                simInput.Array.SetCellCenterpoints(Width, Height); //added code here
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message, "Error loading layout texture", MessageBoxButtons.OK);
                simInput.Array.LayoutTexture = origTexture;
            }
            CalculateSimStepGui();
        }

        private void LoadModel(Mesh mesh, String name)
        {
            Vector3 size = mesh.BoundingBox.Max - mesh.BoundingBox.Min;
            toolStripStatusLabel.Text = string.Format("Loaded model {0}, {1} triangles, {2:0.00}x{3:0.00}x{4:0.00}m",
                name,
                mesh.triangles.Length,
                size.X, size.Y, size.Z);
            SetModel(mesh);
        }

        //private void LoadModel(string filename) {
        //    Mesh mesh = LoadMesh(filename);
        //    Vector3 size = mesh.BoundingBox.Max - mesh.BoundingBox.Min;
        //    if (size.Length > 1000)
        //    {
        //        mesh = MeshUtils.Scale(mesh, 0.001f);
        //        size *= 0.001f;
        //    }
        //    toolStripStatusLabel.Text = string.Format("Loaded model {0}, {1} triangles, {2:0.00}x{3:0.00}x{4:0.00}m",
        //        System.IO.Path.GetFileName(filename),
        //        mesh.triangles.Length,
        //        size.X, size.Y, size.Z);
            
        //    SetModel(mesh);
        //}

        //private Mesh LoadMesh(String filename) {
        //    String extension = filename.Split('.').Last().ToLower();
        //    IMeshParser parser;
        //    if (extension.Equals("3dxml")) {
        //        parser = new MeshParser3DXml();
        //    } else if (extension.Equals("stl")) {
        //        parser = new MeshParserStl();
        //    } else {
        //        throw new ArgumentException("Unsupported file type: " + extension);
        //    }
        //    parser.Parse(filename);
        //    return parser.GetMesh();
        //}
        
        /// <summary>
        /// Uses the given mesh for rendering and calculation.
        /// 
        /// Computes shadow volumes for rendering.
        /// </summary>
        private void SetModel(Mesh mesh) {
            // create shadow volumes
            Logger.info("computing shadows...");
            Shadow newShadow = new Shadow(mesh);
            newShadow.Initialize(); // make sure init works before setting shadow
            shadow = newShadow;

            // render them
            ShadowMeshSprite shadowSprite = new ShadowMeshSprite(shadow);
            var center = (mesh.BoundingBox.Max + mesh.BoundingBox.Min) / 2;
            shadowSprite.Position = new Vector4(-center, 1);
            glControl.Sprite = shadowSprite;

            simInput.Array.Mesh = mesh;
        }

        /// <summary>
        /// Responds to simulation input change.
        /// Calculates as much as it can. Interactive, must be fast.
        /// Shadow visualization, not full calc.
        /// </summary>
        private void CalculateSimStepGui() {
            /* compute array power automatically? 
             * currently, only on Compute button press */
            /* update the view */
            UpdateShadowView();
        }

        /// <summary>
        /// Finds the position of the sun, or returns (0,0,0) if it's below the horizon.
        /// </summary>
        private Vector3 CalculateSunDir() {
            var lightDir = ArraySimulator.GetSunDir(simInput);
            // is the sun below the horizon? then it's night, return 0
            if (lightDir.Y < 0) {
                lightDir = Vector3.Zero;
            }
            return lightDir;
        }

        /// <summary>
        /// Updates 3D rendering (view) from sim inputs (model).
        /// </summary>
        private void UpdateShadowView() {
            /* compute the sun's position */
            Vector3 lightDir = CalculateSunDir();
            shadow.Light = new Vector4(lightDir, 0);
            shadow.ComputeShadows();
            glControl.Refresh();
        }

        private void openModelToolStripMenuItem_Click(object sender, EventArgs args) {
            if (openFileDialogModel.ShowDialog() != System.Windows.Forms.DialogResult.OK) {
                return;
            }
            try {
                var fname = openFileDialogModel.FileName;
                var mesh = MeshIO.LoadMesh(fname);
                LoadModel(mesh, Path.GetFileName(fname));
            } catch (Exception e) {
                MessageBox.Show(e.Message, "Error loading model", MessageBoxButtons.OK);
            }
            CalculateSimStepGui();
        }

        private void openLayoutToolStripMenuItem_Click(object sender, EventArgs args) {
            // prompt the user for a texture image
            Debug.Assert(simInput != null && simInput.Array != null);
            Bitmap bitmap = arrayLayoutForm.PromptUserForLayoutTexture();
            if (bitmap == null) {
                return;
            }

            // apply the new texture, rollback if it fails
            Bitmap origTexture = simInput.Array.LayoutTexture;
            try {
                simInput.Array.LayoutTexture = bitmap;
                simInput.Array.ReadStringsFromColors();
                simInput.Array.SetCellCenterpoints(Width, Height); //added code here
            } catch (Exception e) {
                MessageBox.Show(e.Message, "Error loading layout texture", MessageBoxButtons.OK);
                simInput.Array.LayoutTexture = origTexture;
            }
            CalculateSimStepGui();
        }

        /// <summary>
        /// Called when one of the sim input GUIs changes.
        /// </summary>
        private void simInputs_Change(object sender, EventArgs e) {
            CalculateSimStepGui();
        }

        private void btnRecalc_Click(object sender, EventArgs e) {
            try {
                InitBuilder(simInput.Array, simInput.Temperature, simInput.MPPT, simInput.BattPackSpec);
                builder.ClearInsolationData(simInput.Array.Strings);

                InitSimulator();
                ArraySimulationStepOutput simOutputNoon = simulator.Simulate(
                    simInput.Array, new Vector3(0.1f, 0.995f, 0.0f), 
                    simInput.Irradiance, simInput.IndirectIrradiance, 
                    simInput.EncapuslationLoss, simInput.Temperature);
                ArraySimulationStepOutput simOutput = simulator.Simulate(simInput);
                Debug.WriteLine("Finished Sim");

                builder.ReturnBestCells(simInput.Array.Strings);
                arrayLayoutForm = new ArrayLayoutForm(simInput.Array);
                //arrayLayoutForm.ShowDialog();//to check to see what happened.

                //StringSimulator stringSim = new StringSimulator(); //hack
                //double beforePower = StringSimulator.CalcArrayPower(simInput.Array, 1, simInput.Temperature);
                //builder.SetOptimalStrings(simInput.Array.Strings);
                //double afterPower = StringSimulator.CalcArrayPower(simInput.Array, 1, simInput.Temperature);
                //Debug.WriteLine("Before: {0}W, After: {1}W", beforePower, afterPower);

                //using (System.IO.StreamWriter file = new System.IO.StreamWriter(@"C:\Users\John\Dropbox\SolarCar\WriteLines3.txt", true))
                //{
                //    int nstrings = simInput.Array.Strings.Count;
                //    string printToLine = "";
                //    for (int i = 0; i < nstrings; i++)
                //    {
                //        var cellStr = simInput.Array.Strings[i];
                //        printToLine = "New String";
                //        file.WriteLine(printToLine);
                //        for (int j = 0; j < cellStr.Cells.Count; j++)
                //        {
                //            printToLine = cellStr.Cells[j].Location.ToString() + ": ";
                //            if (cellStr.Cells[j].isClusterCenter) printToLine = "Cluster Center ->" + printToLine;
                //            int insolCount = cellStr.Cells[j].Insolation.Count();
                //            for (int k = 0; k < insolCount; k++) printToLine += Math.Round(cellStr.Cells[j].Insolation[k], 3) + ", ";
                //            file.WriteLine(printToLine);
                //        }
                //    }
                //}

                double arrayAreaDistortion = Math.Abs(simOutputNoon.ArrayLitArea-simOutput.ArrayArea)/simOutput.ArrayArea;
                
                Debug.WriteLine("Array simulation output");
                Debug.WriteLine("   ... " + simOutput.ArrayArea + " m^2 nominal area, "
                     + simOutputNoon.ArrayLitArea + " m^2 lit area" + (arrayAreaDistortion > 0.01 ? " MISMATCH" : ""));
                Debug.WriteLine("   ... " + simOutput.ArrayLitArea + " m^2 exposed to sunlight");
                Debug.WriteLine("   ... " + simOutput.WattsInsolation + " W insolation");
                Debug.WriteLine("   ... " + simOutput.WattsOutputByCell + " W output (assuming mppt per cell)");
                Debug.WriteLine("   ... " + simOutput.WattsOutput + " W output");

                //update ui
                String boldLine = string.Format("{0:0}W over {1:0.00}m\u00B2 cell area", 
                    simOutput.WattsOutput, simOutput.ArrayArea);
                String firstLine = string.Format(", {0:0.00}m\u00B2 lit cells{1}, {2:0.00}m\u00B2 shaded",
                    simOutputNoon.ArrayLitArea, arrayAreaDistortion>0.01 ? " (MISMATCH)":"", simOutputNoon.ArrayLitArea-simOutput.ArrayLitArea);
                String secondLine = string.Format("(Power breakdown: {0:0}W {1:0}% in, {2:0}W {3:0}% ideal mppt, {4:0}W {5:0}% output)",
                    simOutput.WattsInsolation, simOutput.WattsInsolation / simOutputNoon.WattsInsolation * 100,
                    simOutput.WattsOutputByCell, simOutput.WattsOutputByCell / simOutputNoon.WattsOutputByCell * 100,
                    simOutput.WattsOutput, simOutput.WattsOutput / simOutputNoon.WattsOutput * 100);
                this.labelArrPower.Rtf = @"{\rtf1\ansi\deff0 {\b "+boldLine+@"}"+firstLine
                    +@"\line "+ secondLine+"}";

                outputStringsListBox.Items.Clear();
                outputStringsListBox.Items.AddRange(simOutput.Strings);
            } catch (Exception exc) {
                MessageBox.Show(exc.Message);
            }
        }

        private void layoutToolStripMenuItem_Click(object sender, EventArgs e) {
            arrayLayoutForm = new ArrayLayoutForm(simInput.Array);
            arrayLayoutForm.ShowDialog();
        }

        private void cellParametersToolStripMenuItem_Click(object sender, EventArgs e) {
            cellParamsForm.ShowDialog();
        }

        private void layoutTextureDimensionsToolStripMenuItem_Click(object sender, EventArgs e) {
            if (arrayDimsForm != null && !arrayDimsForm.IsDisposed) {
                arrayDimsForm.BringToFront();
            } else {
                arrayDimsForm = new ArrayDimensionsForm();
                arrayDimsForm.Array = simInput.Array;
                arrayDimsForm.Show();
            }
        }

        private void outputStringsListBox_SelectedIndexChanged(object sender, EventArgs e) {
            ArraySimStringOutput output = (ArraySimStringOutput)outputStringsListBox.SelectedItem;
            if (output == null) return;

            // show details
            outputStringLabel.Text = "" + output.String;
            outputStringInsolationLabel.Text = string.Format("{0:0.0} W", 
                output.WattsIn);
            outputStringPowerLabel.Text = string.Format("{0:0.0} W ({1:0.0} %)", 
                output.WattsOutput, 100*output.WattsOutput/output.WattsOutputIdeal);
            outputStringPerfectMPPTLabel.Text = string.Format("{0:0.0} W ({1:0.0} %)",
                output.WattsOutputByCell, 100 * output.WattsOutputByCell / output.WattsOutputIdeal);
            outputStringFlattenedLabel.Text = string.Format("{0:0.0} W",
                output.WattsOutputIdeal);
            outputStringAreaLabel.Text = string.Format("{0:0.000} m^2",
                output.Area);
            outputStringShadedLabel.Text = string.Format("{0:0.000} m^2 ({1:0.0} %)",
                output.AreaShaded, 100*output.AreaShaded / output.Area);
            
            // show it on the layout
            outputArrayLayoutControl.CellString = output.String;
        }

        private void saveLayoutTextureToolStripMenuItem_Click(object sender, EventArgs e) {
            if(simInput.Array.LayoutTexture == null){
                MessageBox.Show("Nothing to save. Try opening and editing a layout first.");
                return;
            }
            DialogResult result = saveFileDialogLayout.ShowDialog();
            if (result != DialogResult.OK) return;
            simInput.Array.LayoutTexture.Save(saveFileDialogLayout.FileName);
        }

        private void outputStringIVLinkLabel_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e) {
            ArraySimStringOutput output = (ArraySimStringOutput)outputStringsListBox.SelectedItem;
            if(output == null) {
                MessageBox.Show("No string selected.");
                return;
            }

            var form = new IVTraceForm();
            form.Label = output.String.ToString();
            form.IVTrace = output.IVTrace;
            form.Show();
        }

        private void bypassDiodeParametersToolStripMenuItem_Click(object sender, EventArgs e) {
            var form = new BypassDiodesForm();
            form.Spec = simInput.Array.BypassDiodeSpec;
            form.ShowDialog();
        }
        private void mPPTParametersToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var form = new MPPT_ParamForm(simInput.MPPT);
            form.ShowDialog();
        }


        private void dateTimePicker1_ValueChanged(object sender, EventArgs e) {
            if (dateTimePicker2.Value <= dateTimePicker1.Value) {
                dateTimePicker2.Value = dateTimePicker1.Value.AddHours(1);
            }
        }

        private void dateTimePicker2_ValueChanged(object sender, EventArgs e) {
            if (dateTimePicker2.Value <= dateTimePicker1.Value) {
                dateTimePicker1.Value = dateTimePicker2.Value.AddHours(-1);
            }
        }

        private void buttonRun_Click_1(object sender, EventArgs e) {
            TimeAveragedSim();
        }

        private void timeStepBox_KeyPress(object sender, System.Windows.Forms.KeyPressEventArgs e)
        {
            char ch = e.KeyChar;
            if (!Char.IsDigit(ch) && ch != 8 && ch != 46) e.Handled = true; //8 handles backspace key
        }
        private void TimeAveragedSim() {
            // input time range; all other inputs come from simInput
            DateTime utcStart = dateTimePicker1.Value.Subtract(simInput.Timezone.GetUtcOffset(dateTimePicker1.Value));
            DateTime utcEnd = dateTimePicker2.Value.Subtract(simInput.Timezone.GetUtcOffset(dateTimePicker2.Value));

            // step-by-step output
            TextWriter csv = new StreamWriter("../../../../output.csv");
            csv.WriteLine("time_utc,insolation_w,output_w");

            // average output
            var simAvg = new ArraySimulationStepOutput();

            // simulate in 10-minute intervals
            InitSimulator();
            int nsim = 0;
            int stepSize = Convert.ToInt32(timeStepBox.Text);
            for (DateTime time = utcStart; time <= utcEnd; time = time.AddMinutes(stepSize), nsim++)
            {
                simInput.Utc = time;
                simInputControls.UpdateView();
                ArraySimulationStepOutput simOutput = simulator.Simulate(simInput);

                // averate the outputs
                if (nsim > 0) {
                    Debug.Assert(simAvg.ArrayArea == simOutput.ArrayArea);
                }
                simAvg.ArrayArea = simOutput.ArrayArea;
                simAvg.ArrayLitArea += simOutput.ArrayLitArea;
                simAvg.WattsInsolation += simOutput.WattsInsolation;
                simAvg.WattsOutputByCell += simOutput.WattsOutputByCell;
                simAvg.WattsOutput += simOutput.WattsOutput;

                // debug output
                csv.WriteLine(time + "," + simOutput.WattsInsolation + "," + simOutput.WattsOutput);
            }
            InitBuilder(simInput.Array, simInput.Temperature, simInput.MPPT, simInput.BattPackSpec);
            builder.ReturnBestCells(simInput.Array.Strings);
            //arrayLayoutForm.ShowDialog();//to check to see what happened.

            //builder.ClusterIntoStrings(simInput.Array.Strings);
            //builder.SetOptimalStrings(simInput.Array.Strings);
            csv.Close();

            nsim = 0;
            //StringSimulator stringSim = new StringSimulator();
            double totalPowerAfter = 0;
            for (DateTime time = utcStart; time <= utcEnd; time = time.AddMinutes(stepSize), nsim++)
            {
                totalPowerAfter += StringSimulator.CalcArrayPower(simInput.Array, nsim, simInput.Temperature);
            }

            //using (System.IO.StreamWriter file = new System.IO.StreamWriter(@"C:\Users\John\Dropbox\SolarCar\WriteLines4.txt", true))
            //{
            //    int nstrings = simInput.Array.Strings.Count;
            //    string printToLine = "X, Y, ";
            //    int insolCount = simInput.Array.Strings[0].Cells[0].Insolation.Count();
            //    for (DateTime time = utcStart; time <= utcEnd; time = time.AddMinutes(stepSize), nsim++) printToLine += time.Hour.ToString()+ ", ";
            //    file.WriteLine(printToLine);
            //    for (int i = 0; i < nstrings; i++)
            //    {
            //        var cellStr = simInput.Array.Strings[i];
            //        //printToLine = "New String";
            //        //file.WriteLine(printToLine);
            //        for (int j = 0; j < cellStr.Cells.Count; j++)
            //        {
            //            printToLine = cellStr.Cells[j].Location.X.ToString() + ", " + cellStr.Cells[j].Location.Y.ToString() + ", ";
            //            //if (cellStr.Cells[j].isClusterCenter) printToLine = "Cluster Center ->" + printToLine;
            //            insolCount = cellStr.Cells[j].Insolation.Count();
            //            for (int k = 0; k < insolCount; k++) printToLine += Math.Round(cellStr.Cells[j].Insolation[k], 3) + ", ";
            //            file.WriteLine(printToLine);
            //        }
            //    }
            //}


            Debug.WriteLine("Before: {0}, After: {1}", simAvg.WattsOutput, totalPowerAfter);

            // show the average output
            simAvg.ArrayLitArea /= nsim;
            simAvg.WattsInsolation /= nsim;
            simAvg.WattsOutputByCell /= nsim;
            simAvg.WattsOutput /= nsim;
            Debug.WriteLine("Array time-averaged simulation output");
            Debug.WriteLine("   ... " + simAvg.ArrayArea + " m^2 total cell area");
            Debug.WriteLine("   ... " + simAvg.ArrayLitArea + " m^2 exposed to sunlight");
            Debug.WriteLine("   ... " + simAvg.WattsInsolation + " W insolation");
            Debug.WriteLine("   ... " + simAvg.WattsOutputByCell + " W output (assuming mppt per cell)");
            Debug.WriteLine("   ... " + simAvg.WattsOutput + " W output");

        }

        //what happens if you leave it blank
        private void timeStepBox_Validated(object sender, EventArgs e)
        {
            if (((TextBox)sender).Text == "") ((TextBox)sender).Text = "10";
        }
    }
}
