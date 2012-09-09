﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Xml;
using OpenTK;

namespace SSCP.ShellPower {
    public partial class MainForm : Form {
        /* model */
        Mesh mesh, amended;
        bool[] trisInArray;
        Shadow shadow;

        /* view */
        //KDTree<MeshTriangle> kdTree;
        ShadowMeshSprite shadowSprite;

        /* data i/o */
        GeoNames geoNamesApi = new GeoNames();

        /* simulation state */
        ArraySimulationStepInput simInput = new ArraySimulationStepInput();
        ArraySimulationStepOutput simOutput = new ArraySimulationStepOutput();

        public MainForm() {
            InitializeComponent();
            GuiSimStepInputs(null, null);
        }

        void LoadModel(string filename) {
            Mesh mesh = LoadMesh(filename);
            toolStripStatusLabel.Text = string.Format("Loaded model {0}, {1} triangles",
                System.IO.Path.GetFileName(filename),
                mesh.triangles.Length);
            SetModel(mesh);
        }

        private IArea GetArrayArea() {
            float arrYS = 0.75f; // side
            float arrYB = 0.3f; // bubble
            float arrXF = -2.0f, arrXR = 2.3f; // +X is the rear
            float arrXBF = -1.0f, arrXBR = 1.0f; // bubble
            float gap = 0.0f, gap2 = gap / 2;
            Polygon2 arr1 = new Polygon2();
            arr1.vertices = new Vector2[]{
                new Vector2(arrXF, -arrYS),
                new Vector2(arrXF, -gap2),
                new Vector2(arrXBF-gap, -gap2),
                new Vector2(arrXBF-gap, -arrYS)};
            Polygon2 arr2 = new Polygon2();
            arr2.vertices = new Vector2[]{
                new Vector2(arrXF, arrYS),
                new Vector2(arrXF, gap2),
                new Vector2(arrXBF-gap, gap2),
                new Vector2(arrXBF-gap, arrYS)};
            Polygon2 arr3 = new Polygon2();
            arr3.vertices = new Vector2[]{
                new Vector2(arrXBF, -arrYS),
                new Vector2(arrXBF, -arrYB),
                new Vector2(arrXBR, -arrYB),
                new Vector2(arrXBR, -arrYS)};
            Polygon2 arr4 = new Polygon2();
            arr4.vertices = new Vector2[]{
                new Vector2(arrXBF, arrYS),
                new Vector2(arrXBF, arrYB),
                new Vector2(arrXBR, arrYB),
                new Vector2(arrXBR, arrYS)};
            Polygon2 arr5 = new Polygon2();
            arr5.vertices = new Vector2[]{
                new Vector2(arrXR, -arrYS),
                new Vector2(arrXR, -gap2),
                new Vector2(arrXBR+gap, -gap2),
                new Vector2(arrXBR+gap, -arrYS)};
            Polygon2 arr6 = new Polygon2();
            arr6.vertices = new Vector2[]{
                new Vector2(arrXR, arrYS),
                new Vector2(arrXR, gap2),
                new Vector2(arrXBR+gap, gap2),
                new Vector2(arrXBR+gap, arrYS)};

            CompoundShape2 arrs = new CompoundShape2();
            arrs.include.Add(arr1);
            arrs.include.Add(arr2);
            arrs.include.Add(arr3);
            arrs.include.Add(arr4);
            arrs.include.Add(arr5);
            arrs.include.Add(arr6);
            return arrs;
        }

        private Mesh LoadMesh(String filename) {
            String extension = filename.Split('.').Last().ToLower();
            IMeshParser parser;
            if (extension.Equals("3dxml")) {
                parser = new MeshParser3DXml();
            } else if (extension.Equals("stl")) {
                parser = new MeshParserStl();
            } else {
                throw new ArgumentException("unsupported filetype: " + extension);
            }
            parser.Parse(filename);
            return parser.GetMesh();
        }

        private void SetModel(Mesh mesh) {
            this.mesh = mesh;

            //split out the array
            Logger.info("creating solar array boundary in the mesh...");

            /* sunbad */
            var area = GetArrayArea();
            ExtrudedVolume vol = new ExtrudedVolume() {
                area = area,
                plane = ExtrudedVolume.Plane.XZ
            };
            MeshUtils.Split(mesh, vol, out amended, out trisInArray);
            Logger.info("mesh now has " + amended.points.Length + " verts, " + amended.triangles.Length + " tris");

            //create kd tree
            /*Logger.info("creating kd tree...");
            var tris = mesh.triangles
                .Select((tri, ix) => new MeshTriangle() {
                    Mesh = amended,
                    Triangle = ix
                })
                .ToList();
            kdTree = new KDTree<MeshTriangle>();
            kdTree.AddAll(tris);*/

            //create shadows volumes
            Logger.info("computing shadows...");
            shadow = new Shadow(amended);
            shadow.Initialize();

            // color the array green
            Logger.info("creating shadow sprite");
            shadowSprite = new ShadowMeshSprite(shadow);
            int nt = amended.triangles.Length;
            shadowSprite.FaceColors = new Vector4[nt];
            var green = new Vector4(0.3f, 0.8f, 0.3f, 1f);
            var white = new Vector4(1f, 1f, 1f, 1f);
            for (int i = 0; i < nt; i++) {
                shadowSprite.FaceColors[i] = trisInArray[i] ? green : white;
            }

            //render the mesh, with shadows, centered in the viewport
            var center = (amended.BoundingBox.Max + amended.BoundingBox.Min) / 2;
            shadowSprite.Position = new Vector4(-center, 1);
            glControl.Sprite = shadowSprite;
        }

        private void CalculateSimStep() {
            // update the astronomy model
            var utc_time = simInput.LocalTime - new TimeSpan((long)(simInput.Timezone * 3600.0) * 10000000);
            var sidereal = Astro.sidereal_time(
                utc_time,
                simInput.Longitude);
            var azimuth = Astro.solar_azimuth(
                (int)sidereal.TimeOfDay.TotalSeconds,
                simInput.LocalTime.DayOfYear,
                simInput.Latitude)
                - (float)simInput.Heading;
            var elevation = Astro.solar_elevation(
                (int)sidereal.TimeOfDay.TotalSeconds,
                simInput.LocalTime.DayOfYear,
                simInput.Latitude);
            Logger.info("sim step\n\t" +
                "lat {0:0.0} lon {1:0.0} heading {2:0.0}\n\t" +
                "azith {3:0.0} elev {4:0.0} utc {5} sidereal {6}",
                simInput.Latitude,
                simInput.Longitude,
                Astro.rad2deg(simInput.Heading),
                Astro.rad2deg(azimuth),
                Astro.rad2deg(elevation),
                utc_time,
                sidereal);

            //recalculate the shadows
            var lightDir = new Vector3(
                (float)(-Math.Cos(elevation) * Math.Cos(azimuth)), (float)(Math.Sin(elevation)),
                (float)(-Math.Cos(elevation) * Math.Sin(azimuth)));
            if (elevation < 0) {
                lightDir = Vector3.Zero;
            }
            if (shadow != null && lightDir.LengthSquared > 0) {
                shadow.Light = new Vector4(lightDir, 0);
                shadow.ComputeShadows();
            }

            // update the view
            glControl.SunDirection = lightDir;

            // calculate array params
            //TODO: fix this hackery
            const float insolation = 1000f; // W/m^2
            const float efficiency = 0.227f; 
            float arrayArea = 0.0f, shadedArea = 0.0f, totalWatts = 0.0f;
            int nt = amended == null ? 0 : amended.triangles.Length;
            for (int i = 0; i < nt; i++) {
                if (!trisInArray[i]) {
                    continue;
                }
                var tri = amended.triangles[i];
                var vA = amended.points[tri.vertexA];
                var vB = amended.points[tri.vertexB];
                var vC = amended.points[tri.vertexC];
                float area = Vector3.Cross(vC - vA, vB - vA).Length / 2;
                arrayArea += area;

                int nshad = 0;
                if (shadow.VertShadows[tri.vertexA]) nshad++;
                if (shadow.VertShadows[tri.vertexB]) nshad++;
                if (shadow.VertShadows[tri.vertexC]) nshad++;
                shadedArea += area * nshad / 3.0f;

                // if we're not in a shadow, get cosine rule insolation.
                if (nshad < 2) {
                    var cosInsolation = Math.Max(Vector3.Dot(tri.normal, lightDir), 0f) * insolation;
                    var watts = cosInsolation * efficiency * area;
                    totalWatts += watts;
                }
            }

            //update ui
            this.labelArrPower.Text = string.Format(
                "{0:0}W over {1:0.00}m\u00B2, {2:0.00}m\u00B2 shaded",
                totalWatts, arrayArea, shadedArea);
        }

        /// <summary>
        /// Updates 3D rendering (view) from environment (model).
        /// </summary>
        private void RefreshModelView() {
            glControl.Refresh();
        }

        /// <summary>
        /// Updates sim controls (view) from sim state (model).
        /// </summary>
        private void UpdateSimStateView() {
            /* set heading */
            string[] headings = { "N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE", "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW" };
            int dirIx = (int)Math.Round(simInput.Heading / (2 * Math.PI) * 16);
            if (dirIx >= headings.Length) dirIx -= headings.Length;
            labelCarDirection.Text = headings[dirIx];

            /* set date/time */
            dateTimePicker.Value = simInput.Utc; // fix roundoff problems
            labelTimezone.Text = string.Format("GMT{0}{1:0.0}", simInput.Timezone >= 0 ? "+" : "", simInput.Timezone);
            var name = geoNamesApi.GetTimezoneName(simInput.Latitude, simInput.Longitude);
            if (name != null)
                labelTimezone.Text += " " + name;
            labelLocalTime.Text = simInput.LocalTime.ToString("HH:mm:ss");
            trackBarTimeOfDay.Value = (int)(simInput.LocalTime.TimeOfDay.TotalHours * (trackBarTimeOfDay.Maximum + 1) / 24);
        }

        private void openModelToolStripMenuItem_Click(object sender, EventArgs e) {
            if (openFileDialogModel.ShowDialog() == System.Windows.Forms.DialogResult.OK) {
                LoadModel(openFileDialogModel.FileName);
                CalculateSimStep();
                RefreshModelView();
            }
        }

        private void openLayoutToolStripMenuItem_Click(object sender, EventArgs e) {

        }

        private void openSimParamsToolStripMenuItem_Click(object sender, EventArgs e) {

        }

        private void GuiSimStepInputs(object sender, EventArgs e) {
            var lat = double.Parse(textBoxLat.Text);
            var lon = double.Parse(textBoxLon.Text);

            /* get timezone */
            var tz = geoNamesApi.GetTimezone(lat, lon);

            /* get local time */
            DateTime utcTime = dateTimePicker.Value;
            DateTime localTime = utcTime + new TimeSpan((long)(tz * 60 * 60 * 10000000));

            /* get car orientation */
            double dir = 2 * Math.PI * trackBarCarDirection.Value / (trackBarCarDirection.Maximum + 1);

            /* set direction */
            double heading = 2 * Math.PI * trackBarCarDirection.Value / (trackBarCarDirection.Maximum + 1);

            /* update sim input */
            simInput.Heading = heading;
            simInput.Latitude = lat;
            simInput.Longitude = lon;
            simInput.Timezone = tz;
            simInput.LocalTime = localTime;
            simInput.Utc = utcTime;
            UpdateSimStateView();
            CalculateSimStep();
            RefreshModelView();
        }

        private void buttonRun_Click(object sender, EventArgs e) {

        }

        private void buttonAnimate_Click(object sender, EventArgs e) {

        }

        private void trackBarTimeOfDay_Scroll(object sender, EventArgs e) {
            double hours = (double)trackBarTimeOfDay.Value / (trackBarTimeOfDay.Maximum + 1) * 24;
            var timeOfDay = new TimeSpan((long)(hours * 60 * 60 * 10000000) + 1);
            simInput.LocalTime = simInput.LocalTime.Date + timeOfDay;
            simInput.Utc = simInput.LocalTime - new TimeSpan((long)(simInput.Timezone * 60 * 60 * 10000000));
            UpdateSimStateView();
        }
    }
}
