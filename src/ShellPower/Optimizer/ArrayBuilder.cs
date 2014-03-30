using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Diagnostics;
using System.Windows.Forms;

namespace SSCP.ShellPower
{
    class ArrayBuilder
    {
        ArraySpec tempArray;
        ArrayLayoutForm arrayLayoutForm;
        private int MIN_NUM_CLUSTERS = 4;
        private int MAX_NUM_CLUSTERS = 16;
        private static DiodeSpec byPassDiodeSpec;
        private static CellSpec cellSpec;
        private static double cTempLocal;
        private int MAX_LOOP_TIMES = 1000;
        private int MaxNumCells = 391;//change this later

        public ArrayBuilder(ArraySpec originalArray, double cTemp)
        {
            if (originalArray == null) throw new ArgumentException("No array specified.");
            if (originalArray.Mesh == null) throw new ArgumentException("No array shape (mesh) loaded.");
            if (originalArray.LayoutTexture == null) throw new ArgumentException("No array layout (texture) loaded.");
            tempArray = new ArraySpec();
            tempArray.LayoutTexture = originalArray.LayoutTexture;
            tempArray.Mesh = originalArray.Mesh;
            tempArray.LayoutBoundsXZ = originalArray.LayoutBoundsXZ;
            byPassDiodeSpec = originalArray.BypassDiodeSpec;
            cellSpec = originalArray.CellSpec;
            cTempLocal = cTemp;
        }

        private class CellCluster
        {
            public ArraySpec.Cell Centroid { get; set; } 
            public List<ArraySpec.Cell> Cluster { get; set; }
        }

        //private class CenterCell{
        //    private PointF Location { get; set; } 
        //    private List<double> Insolation { get; set; }
        //    private CenterCell() {
        //        Location = new PointF(0,0);
        //        Insolation = new List<double>();
        //    }
        //}

        /// <summary>
        /// Deletes all the insolation data from each cell in list of strings passed in
        /// </summary>
        public void ClearInsolationData(List<ArraySpec.CellString> strings)
        {
            foreach (ArraySpec.CellString cellStr in strings)
            {
                foreach (ArraySpec.Cell cell in cellStr.Cells)
                {
                    cell.Insolation.Clear();
                }
            }
        }

        public void ClusterIntoStrings(List<ArraySpec.CellString> strings)
        {
            var cells = StringsToCells(strings);

            int k = 6; //should be a for loop here
            double locScale = .015;//should get this from the UI
            Debug.WriteLine("about to enter kmeans");
            List<CellCluster> clusters = kMeansCluster(k, cells, locScale);

            //assign the clusters back to the array
            ClustersToStrings(clusters, strings);
        }

        private void ClustersToStrings(List<CellCluster> clusters, List<ArraySpec.CellString> strings)
        {
            //assign the clusters back to the array
            strings.Clear();
            int i = 0;
            foreach (CellCluster cluster in clusters)
            {
                ArraySpec.CellString newString = new ArraySpec.CellString();
                newString.Name = i.ToString();
                newString.Cells = cluster.Cluster; //in no particular order right now.
                strings.Add(newString);
                i++;
            }
        }

        private List<ArraySpec.Cell> StringsToCells(List<ArraySpec.CellString> strings)
        {
            var cells = new List<ArraySpec.Cell>();
            foreach (ArraySpec.CellString cellStr in strings)
            {
                foreach (ArraySpec.Cell cell in cellStr.Cells)
                {
                    cells.Add(cell);
                }
            }
            return cells;
        }

        private List<ArraySpec.Cell> FindBestCells(List<ArraySpec.Cell> cells)
        {
            //faster to get rid cells with the least amount of sun
            int numCells = cells.Count();
            int numCellsToRemove = numCells - MaxNumCells;
            Debug.WriteLine("Removing {0} cells", numCellsToRemove);
            for (int i = 0; i < numCellsToRemove; i++)
            {
                cells.Remove(MinCell(cells, 0, 1));
            }
            Debug.WriteLine("Removed {0} cells", numCells - cells.Count());
            return cells;
        }

        public void ReturnBestCells(List<ArraySpec.CellString> strings)
        {
            var cells = StringsToCells(strings);
            cells = FindBestCells(cells);
            ClustersToStrings(IntializeKMeans(6, cells), strings);
        }

        //public void FindNumMPPTs(List<ArraySpec.Cell> cells)
        //{
        //    //int numCells = cells.Count();
        //    //should be a for loop here
        //    double locScale = .015;//should get this from the UI
        //    Debug.WriteLine("about to enter kmeans");
        //    for(int k = MIN_NUM_CLUSTERS; k < MAX_NUM_CLUSTERS; k++){
        //        List<CellCluster> clusters = kMeansCluster(k, cells, locScale);
        //        double totalArrayPower = 0;
        //        foreach(CellCluster cluster in clusters){
        //            ArraySpec.CellString cellStr = new ArraySpec.CellString();
        //            cellStr.Cells = cluster.Cluster;
        //            int numPeriods = cellStr.Cells[0].Insolation.Count();
        //            for (int i = 0; i < numPeriods; i++)
        //            {
        //                IVTrace clusterIV = StringSimulator.GetStringIV(cellStr, i, cTempLocal, byPassDiodeSpec, cellSpec);
        //                if (clusterIV.Voc > BatterPack.Vmax || clusterIV.Isc > MPPT.Imax || clusterIV.Voc > MPPT.Vmax)
        //                {
        //                    //not a vald configuration!!
        //                    //could split into substrings untiil it is valid
        //                }
        //                totalArrayPower += clusterIV.Pmp * MPPTefficiency
        //            }
        //        }
        //    }
        //}

        /// <summary>
        /// Calculates the Euclidean Distance between two vectors which are:
        /// the insolation stored in the cells that are passed in
        /// returns the distance
        /// </summary>
        private double CalcSqrEuclidDistInsolation(ArraySpec.Cell cell1, ArraySpec.Cell cell2)
        {
            double distance = 0;
            int num1Times = cell1.Insolation.Count;
            int num2Times = cell2.Insolation.Count;
            if (num1Times != num2Times)
            {
                //Something is wrong!!
                throw new ArgumentException("Cells must have the same length Insolation count", "cell2");
            }
            for (int i = 0; i < num1Times; i++)
            {
                try
                {
                    double insolation1 = cell1.Insolation[i];
                    double insolation2 = cell2.Insolation[i];
                    distance += Math.Pow(insolation1 - insolation2, 2); //squares
                }
                catch (System.IndexOutOfRangeException e)
                {
                    throw new System.ArgumentOutOfRangeException("Index is out or range.", e);
                }
            }
            return Math.Sqrt(distance);
        }

        /// <summary>
        /// Calculates the Euclidean Distance between two vectors which are:
        /// the location of the cells
        /// returns the distance
        /// </summary>
        private double CalcSqrEuclidDistLocation(ArraySpec.Cell cell1, ArraySpec.Cell cell2)
        {
            double distance = 0;
            try
            {
                double loc1x = System.Convert.ToDouble(cell1.Location.X);
                double loc1y = System.Convert.ToDouble(cell1.Location.Y);
                double loc2x = System.Convert.ToDouble(cell2.Location.X);
                double loc2y = System.Convert.ToDouble(cell2.Location.Y);
                distance += Math.Pow(loc1x - loc2x, 2); //squares
                distance += Math.Pow(loc1y - loc2y, 2); //square
            }
            catch (Exception ex) { throw ex; }
            return Math.Sqrt(distance);
        }

        /// <summary>
        /// Allows you to visualize each cluster of cells (centers should be orange)
        /// Calls the ArrayLayoutForm and represents each cluster as a new string
        /// connects the cells in the order they were added to the cluster
        /// </summary>
        private void DisplayClusters(List<CellCluster> clusters)
        {
            if (clusters == null) throw new ArgumentException("No clusters to displace.");
            List<ArraySpec.CellString> stringsToShow = new List<ArraySpec.CellString>();
            foreach (CellCluster cluster in clusters)
            {
                ArraySpec.CellString tempString = new ArraySpec.CellString();
                tempString.Cells = cluster.Cluster;
                stringsToShow.Add(tempString);
            }
            tempArray.Strings = stringsToShow;
            arrayLayoutForm = new ArrayLayoutForm(tempArray); // how do I not creat a new one each time?
            arrayLayoutForm.ShowDialog();
        }

        /// <summary>
        /// Allows you to visualize all centers of the clusters in one view
        /// Calls the ArrayLayoutForm and represents each center in a single string
        /// it is a bit of a hack...(centers should be orange/highlited
        /// </summary>
        private void DisplayCenters(List<CellCluster> clusters)
        {
            if (clusters == null) throw new ArgumentException("No clusters to displace.");
            List<ArraySpec.CellString> stringsToShow = new List<ArraySpec.CellString>();
            ArraySpec.CellString tempString = new ArraySpec.CellString();
            foreach (CellCluster cluster in clusters)
            {
                tempString.Cells.Add(cluster.Centroid);
            }
            stringsToShow.Add(tempString);
            tempArray.Strings = stringsToShow;
            arrayLayoutForm = new ArrayLayoutForm(tempArray); // how do I not creat a new one each time?
            arrayLayoutForm.ShowDialog();
        }

        /// <summary>
        /// Clusters the cells into k different clusters
        /// Based on the insolation on each cell and weighting the location distance by locScale
        /// soluation dependent upon intialization as well.
        /// </summary>
        private List<CellCluster> kMeansCluster(int k, List<ArraySpec.Cell> cells, double locScale)
        {
            List<CellCluster> clusters = IntializeKMeans(k, cells);

            //DisplayCenters(clusters);

            Debug.WriteLine("K-intialized with {0} clusters", clusters.Count());
            bool centersChange = true;
            int timesLooped = 0;
            while(centersChange){ //if the center changes, then cells must have changed clusters
                Debug.WriteLine("Has LOOPED {0} times", timesLooped);
                centersChange = false;
                clusters = AssignCellsToClusters(clusters, locScale); //should change clusters
                //Debug.WriteLine("AfterAssign cluster[0] has {0} cells", clusters[0].Cluster.Count());
                int numCellsSwitched = 0;
                string cellNumbers = "";
                foreach(CellCluster cluster in clusters){
                    cellNumbers += cluster.Cluster.Count() + ", "; //MinCell(cluster.Cluster, 0, 1);
                    ArraySpec.Cell newCenter = FindCenter(cluster.Cluster); //does not represent an actual cell
                    newCenter = FindClosestMatchingCell(newCenter, cluster.Cluster, locScale);
                    if(!CellsEqual(newCenter, cluster.Centroid)){
                        centersChange = true;
                        cluster.Centroid.isClusterCenter = false;
                        cluster.Centroid = newCenter;
                        cluster.Centroid.isClusterCenter = true;
                        numCellsSwitched++;
                    }
                }
                Debug.WriteLine(cellNumbers);
                if (timesLooped > MAX_LOOP_TIMES)
                {
                    string message = String.Format("The clustering algorithm has run {0} loops. \n", MAX_LOOP_TIMES);
                    message += "It may not converge, would you like to continue? \n\n";
                    message += "(A different itialization may help)";
                    DialogResult result = MessageBox.Show(message, "Possible Noncergence", MessageBoxButtons.YesNo);
                    if (result == DialogResult.No) break;
                    else timesLooped = 0;
                }
                Debug.WriteLine("{0} centers changed", numCellsSwitched);
                timesLooped++;
            }
            DisplayClusters(clusters);
            Debug.WriteLine("kmeans done!");
            return clusters;
        }

        //private ArraySpec.Cell FindMaxDifCell(List<ArraySpec.Cell> currentCenters, List<ArraySpec.Cell> cells)
        //{
        //    int locScale = 1;
        //    int insolScale = 0;
        //    double max = 0;
        //    double sum = 0;
        //    bool cellInCluster = false;
        //    ArraySpec.Cell furstestCell = new ArraySpec.Cell();
        //    foreach (ArraySpec.Cell cell in cells)
        //    {
        //        sum = 0;
        //        cellInCluster = false;
        //        foreach (ArraySpec.Cell clusterCenter in currentCenters)
        //        {
        //            if (CellsEqual(clusterCenter, cell))
        //            {
        //                cellInCluster = true;
        //            }
        //            sum += CalcSqrEuclidDist(cell, clusterCenter, locScale, insolScale);
        //        }
        //        if (sum > max & !cellInCluster)
        //        {
        //            max = sum;
        //            furstestCell = cell;
        //        }
        //    }
        //    if (furstestCell.Insolation.Count() == 0)
        //    {
        //        Debug.WriteLine("Well Shit!");
        //    }
        //    return furstestCell;
        //}

        /// <summary>
        /// Attempts to return a cell in cells that is farthest from the current Centers passed in
        /// farthest is represented by the Euclidean distance weighting insolation and location by insolScale and locScale respectively
        /// Works by finding the cell that minimizes the ratio of the distance to the furthest center by the distance to the closest center
        /// </summary>
        private ArraySpec.Cell EvenlySpreadCell(List<ArraySpec.Cell> currentCenters, List<ArraySpec.Cell> cells, double locScale = 1, double insolScale = 0)
        {
            double distance = 0;
            double minRatio = Double.MaxValue;
            double ratio = Double.MaxValue;
            bool cellInCluster = false;
            ArraySpec.Cell furstestCell = new ArraySpec.Cell();
            foreach (ArraySpec.Cell cell in cells)
            {
                cellInCluster = false;
                double maxDis = 0;
                double minDis = Double.MaxValue;
                foreach (ArraySpec.Cell clusterCenter in currentCenters)
                {
                    if (CellsEqual(clusterCenter, cell)) cellInCluster = true;
                    distance = CalcSqrEuclidDist(cell, clusterCenter, locScale, insolScale);
                    if (distance > maxDis & !cellInCluster) maxDis = distance;
                    if (distance < minDis & !cellInCluster) minDis = distance;

                }
                ratio = maxDis / minDis;
                if (ratio < minRatio & !cellInCluster)
                {
                    minRatio = ratio;
                    furstestCell = cell;
                }
            }
            Debug.Assert(furstestCell.Insolation.Count() != 0, "furthest cells insolaton is zero!");
            return furstestCell;
        }

        /// <summary>
        /// determines if two cells are equal
        /// if the points and the insolation vector are the same the cells are considered Equal
        /// </summary>
        public bool CellsEqual(ArraySpec.Cell cell1, ArraySpec.Cell cell2)
        {
            if (cell1.Location != cell2.Location) { return false; }
            int num1Times = cell1.Insolation.Count;
            int num2Times = cell2.Insolation.Count;
            if (num1Times != num2Times)
            {
                //Something is wrong!!
                throw new ArgumentException("Cells should have the same length Insolation count", "cell2");
            }
            for (int i = 0; i < num1Times; i++)
            {
                if (cell1.Insolation[i] != cell2.Insolation[i]) { return false; }
            }
            return true;
        }

        
        /// <summary>
        /// intializes the kmeans algrithm with k centers and populates the clusters
        /// clusters are evenly populated based on the order of the cells (but this shouldn't matter)
        /// could randomly select clusters or get them from the seedClusters function
        /// </summary>
        private List<CellCluster> IntializeKMeans(int k, List<ArraySpec.Cell> cells)
        {
            int numCells = cells.Count();
            int cellsPerCluster = numCells/k;//rounding/truncation is ok
            int firstCellToAdd = 0;
            Random rndnum = new Random();
            //List<int> randNumUsed = new List<int>();
            List<ArraySpec.Cell> centroids = new List<ArraySpec.Cell>(); //stores centers used so far
            List<CellCluster> clusters = new List<CellCluster>();
            for (int i = 0; i < k; i++)
            {
                //makes sure that rest of cells get assigned in last loop
                if(i+1 == k){
                    cellsPerCluster = numCells-firstCellToAdd;
                }

                //arbitraily assigns cells to clusters
                CellCluster cluster = new CellCluster();

                ///<uncomment for random>
                //int random = rndnum.Next(numCells);//cells gets smaller each time
                //while (randNumUsed.Contains(random))//ensures that the same cell is not added to another centroid
                //{
                //    random = rndnum.Next(numCells);
                //}
                //randNumUsed.Add(random);
                //cluster.Centroid = cells[random]; //assigns the center to be random
                ///</uncomment for random>
                
                cluster.Centroid = SeedClusters(centroids, cells);
                cluster.Centroid.isClusterCenter = true;
                centroids.Add(cluster.Centroid);

                cluster.Cluster = cells.GetRange(firstCellToAdd,cellsPerCluster); //assigns cells to a cluster
                Debug.WriteLine("Intialized {0} cells to {1} cluster", cluster.Cluster.Count(), i);
                clusters.Add(cluster);
                firstCellToAdd += cellsPerCluster;
            }
            return clusters;
        }

        /// <summary>
        /// Returns cells that are not current Centers and evently distributes them around the surface
        /// First one is MinCell, Second is MaxCell, the rest are evenly Spread
        /// </summary>
        private ArraySpec.Cell SeedClusters(List<ArraySpec.Cell> currentCenters, List<ArraySpec.Cell> cells)
        {
            double locScale = 0;
            double insolScale = 1;
            if (currentCenters.Count() == 0) return MinCell(cells, locScale, insolScale);
            else if (currentCenters.Count() == 1) return MaxCell(cells, locScale, insolScale);
            else return EvenlySpreadCell(currentCenters, cells, locScale, insolScale);
        }

        /// <summary>
        /// Finds the cell closest to the orgin based on the Euclidean distance weighted by the last params
        /// </summary>
        private ArraySpec.Cell MinCell(List<ArraySpec.Cell> cells, double locScale = 1, double insolScale = 0)
        {
            double distance = 0;
            double min = Double.MaxValue;
            ArraySpec.Cell toReturn = new ArraySpec.Cell();
            foreach (ArraySpec.Cell cell in cells)
            {
                distance = DistFromOrigin(cell, locScale, insolScale);
                if (distance < min)
                {
                    min = distance;
                    toReturn = cell;
                }
            }
            Debug.Assert(toReturn != null, "No Min was found!");
            return toReturn;
        }

        /// <summary>
        /// Finds the cell furthest from the orgin based on the Euclidean distance weighted by the last params
        /// </summary>
        private ArraySpec.Cell MaxCell(List<ArraySpec.Cell> cells, double locScale = 1, double insolScale = 0)
        {
            double distance = 0;
            double max = 0;
            ArraySpec.Cell toReturn = new ArraySpec.Cell();
            foreach (ArraySpec.Cell cell in cells){
                distance = DistFromOrigin(cell, locScale, insolScale);
                if (distance > max){
                    max = distance;
                    toReturn = cell;
                }
            }
            Debug.Assert(toReturn != null, "No Max was found!");
            return toReturn;
        }

        /// <summary>
        /// Assigns each cell to the cluster with the centroid that is clostest to that cell
        /// </summary>
        private List<CellCluster> AssignCellsToClusters(List<CellCluster> clusters, double locScale) 
        {
            //create a copy of of the clusters with the same centroids?
            //add new elements to the to that new one
            int k = clusters.Count(); 
            List<CellCluster> newClusters = new List<CellCluster>();
            for (int i = 0; i < k; i++)
            {
                CellCluster clusterToAdd = new CellCluster();
                clusterToAdd.Centroid = clusters[i].Centroid;
                clusterToAdd.Cluster = new List<ArraySpec.Cell>();
                newClusters.Add(clusterToAdd);
            }
            //loop through all cells
            foreach (CellCluster cellCluster in clusters)
            {
                foreach (ArraySpec.Cell cell in cellCluster.Cluster)
                {
                    //intialize min and the cluster to add to
                    double min = CalcSqrEuclidDist(newClusters[0].Centroid, cell, locScale);
                    CellCluster clusterToAddTo = newClusters[0];
                    //adds the cells to the new cluster that is closest to it
                    foreach (CellCluster centerCluster in newClusters)
                    {
                        double distance = CalcSqrEuclidDist(centerCluster.Centroid, cell, locScale);
                        if (distance < min)
                        {
                            min = distance;
                            clusterToAddTo = centerCluster;
                        }
                    }
                    clusterToAddTo.Cluster.Add(cell);
                }
            }
            //if no cells have been assigned to a cluster, that's bad!
            foreach (CellCluster cluster in newClusters)
            {
                Debug.Assert(cluster.Cluster.Count() != 0, "cluster has 0 cells!!!!!");
            }
            return newClusters;
        }

        /// <summary>
        /// returns the cell that is clostest (distance) to the representation of the other cell passed in
        /// loops through the list of cells passed in and returns one of those
        /// </summary>
        private ArraySpec.Cell FindClosestMatchingCell(ArraySpec.Cell center, List<ArraySpec.Cell> cells, double locScale)
        {
            ArraySpec.Cell closestCell = cells[0];
            double min = CalcSqrEuclidDist(center, cells[0], locScale);
            double distance = min;
            for (int i = 1; i < cells.Count(); i++) //starts at 1 because just calculated the first one
            {
                distance = CalcSqrEuclidDist(center, cells[i], locScale);
                if (distance < min)
                {
                    closestCell = cells[i];
                    min = distance;
                }
            }
            return closestCell;
        }

        /// <summary>
        /// gets the Euclidean Distance between two cells
        /// adjust the scale of the position distance and insolation distance
        /// </summary>
        private double CalcSqrEuclidDist(ArraySpec.Cell cell1, ArraySpec.Cell cell2, double locationScale, double insolScale = 1.0)
        { 
            double distance = 0;
            distance += CalcSqrEuclidDistInsolation(cell1, cell2) * insolScale;
            distance += CalcSqrEuclidDistLocation(cell1, cell2) * locationScale;
            return distance;
        }

        /// <summary>
        /// gets the Euclidean Distance from the origin or a cell at location (0,0) and 0 insulation
        /// </summary>
        private double DistFromOrigin(ArraySpec.Cell cell, double locScale, double insolScale)
        {
            ArraySpec.Cell originCell = new ArraySpec.Cell();
            originCell.Insolation = new List<double>(Enumerable.Repeat(0.0,cell.Insolation.Count()));
            originCell.Location = new PointF(0, 0);
            return CalcSqrEuclidDist(originCell, cell, locScale, insolScale);
        }

        /// <summary>
        /// finds the center of list of cells and returns that aribirary cell
        /// sums up each point/insolation and divides by the number of cells
        /// </summary>
        private ArraySpec.Cell FindCenter(List<ArraySpec.Cell> cells)
        {
            Debug.Assert(cells.Count() != 0, "There are 0 cells in FindCenter");
            int timePeriods = cells[0].Insolation.Count();
            int numCells = cells.Count();
            //Debug.WriteLine("There are {0} cells with {1} timeperiods", numCells, timePeriods);
            ArraySpec.Cell center = new ArraySpec.Cell();
            List<double> insolationCenter = new List<double>();
            for (int i = 0; i < timePeriods; i++)
            {

                double sum = 0;
                foreach (ArraySpec.Cell cell in cells)
                {
                    sum += cell.Insolation[i];
                }
                double avgInsol = sum/numCells;
                insolationCenter.Add(avgInsol);
            }

            float sumX = 0;
            float sumY = 0;
            foreach (ArraySpec.Cell cell in cells)
            {
                sumX += cell.Location.X;
                sumY += cell.Location.Y;
            }
            center.Insolation = insolationCenter;
            center.Location = new PointF(sumX / numCells, sumY / numCells);
            return center;
        }
    }
}
