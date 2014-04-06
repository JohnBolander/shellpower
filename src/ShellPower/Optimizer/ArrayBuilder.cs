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
        private const int MIN_NUM_CLUSTERS = 4;
        private const int MAX_NUM_CLUSTERS = 16;
        private static DiodeSpec byPassDiodeSpec;
        private static CellSpec cellSpec;
        private static double cTempLocal;
        private const int MAX_LOOP_TIMES = 1000;
        private int MaxNumCells = 391;//change this later
        private static MPPTSpec mpptSpec;
        private static BatterPackSpec packSpec;
        private const double APROX_VMPP_FACTOR = .6; //to estimate the min cells in order to improve run time.
        private int MAX_CELLS_PER_CLUSTER;
        private int MIN_CELLS_PER_CLUSTER;

        private class CellCluster
        {
            public ArraySpec.Cell Centroid { get; set; }
            public List<ArraySpec.Cell> Cluster { get; set; }
        }

        public ArrayBuilder(ArraySpec originalArray, double cTemp, MPPTSpec orgMpptSpec, BatterPackSpec orgPackSpec)
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
            mpptSpec = orgMpptSpec;
            packSpec = orgPackSpec;
            MAX_CELLS_PER_CLUSTER = (int)Math.Floor(Math.Min(mpptSpec.VmaxIn / cellSpec.VocStc, packSpec.Vmax / cellSpec.VocStc));
            MIN_CELLS_PER_CLUSTER = (int)Math.Ceiling(Math.Max(mpptSpec.Vmin / (cellSpec.VocStc*APROX_VMPP_FACTOR), mpptSpec.MaxBR / packSpec.Vmax / (cellSpec.VocStc*APROX_VMPP_FACTOR)));
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

            foreach (CellCluster cluster in clusters)
            {
                cluster.Cluster = OrderCellsForLayout(cluster.Cluster);
            }

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

        /// <summary>
        /// Given all the possible cells, this will select the MaxNumCells number of cells
        /// that are furthest from the origin (the most sun) by calculating the Euclidean 
        /// and only considering the insolation.
        /// </summary>
        /// <param name="cells"></param>
        /// <returns></returns>
        private List<ArraySpec.Cell> FindBestCells(List<ArraySpec.Cell> cells)
        {
            //faster to get rid cells with the least amount of sun
            double locScale = 0;
            double insoScale = 1;
            int numCells = cells.Count();
            int numCellsToRemove = numCells - MaxNumCells;
            Debug.WriteLine("Removing {0} cells", numCellsToRemove);
            for (int i = 0; i < numCellsToRemove; i++)
            {
                cells.Remove(MinCell(cells, locScale, insoScale));
            }
            Debug.WriteLine("Removed {0} cells", numCells - cells.Count());
            return cells;
        }

        /// <summary>
        /// Given the input strings, it alters these strings to only contain the best cells
        /// (as described in FindBestCells) and puts them in 6 arbitrary strings
        /// </summary>
        /// <param name="strings"></param>
        public void ReturnBestCells(List<ArraySpec.CellString> strings)
        {
            var cells = StringsToCells(strings);
            cells = RemoveFlexedCells(cells);
            Debug.WriteLine("Cells left after Removing the non-flexible one: {0}", cells.Count());
            cells = FindBestCells(cells);
            ClustersToStrings(IntializeKMeans(6, cells), strings);
        }

        /// <summary>
        /// Removes cells that are bent too much over the surface of the car. \n
        /// Relies on the cells being marked as true when the simulation is ran.
        /// Currently cells are marked if their ratio of max pixel area to average
        /// exceeds some value.
        /// </summary>
        /// <param name="cells"></param>
        /// <returns></returns>
        private List<ArraySpec.Cell> RemoveFlexedCells(List<ArraySpec.Cell> cells)
        {
            List<ArraySpec.Cell> newCells = new List<ArraySpec.Cell>();
            int numCells = cells.Count();
            for (int i = 0; i < numCells; i++)
            {
                if (!cells[i].isClusterCenter) newCells.Add(cells[i]);
            }
            return newCells;
        }


        public void SetOptimalStrings(List<ArraySpec.CellString> strings)
        {
            var cells = StringsToCells(strings);
            var clusters = FindNumMPPTs(cells);
            foreach (CellCluster cluster in clusters)
            {
                cluster.Cluster = OrderCellsForLayout(cluster.Cluster);
            }
            ClustersToStrings(clusters, strings);
        }


        private List<CellCluster> FindNumMPPTs(List<ArraySpec.Cell> cells)
        {
            //int numCells = cells.Count();
            //should be a for loop here
            double locScale = .015;//should get this from the UI
            double maxPower = 0;
            bool validStrings = true;
            List<CellCluster> bestClusters = new List<CellCluster>();
            Debug.WriteLine("about to enter kmeans");
            for(int k = MIN_NUM_CLUSTERS; k < MAX_NUM_CLUSTERS; k++){
                Debug.WriteLine("Starting Kmeans with {0} strings", k);
                List<CellCluster> clusters = kMeansCluster(k, cells, locScale, true);//constrained
                Debug.WriteLine("Finished Kmeans with {0} strings", k);
                double totalArrayPower = 0;
                validStrings = true;
                int clusterNum = 0;
                foreach(CellCluster cluster in clusters){
                    var validPower = ValidateClusterandGetPower(cluster.Cluster);
                    if (!validPower.Item2 && !validPower.Item2) totalArrayPower += validPower.Item3;
                    else validStrings = false;
                    clusterNum++;
                }
                Debug.WriteLine("{0} strings give {1}W", k, totalArrayPower);
                if (!validStrings) Debug.WriteLine("{0} strings is not a valid config", k);
                if (totalArrayPower > maxPower & validStrings)
                {
                    maxPower = totalArrayPower;
                    ResetCenters(bestClusters);
                    bestClusters = clusters;
                }
                else ResetCenters(clusters);
            }
            return bestClusters;
        }

        /// <summary>
        /// Removes the true node from clusters so that the centers don't
        /// show up on the arraylayout.
        /// </summary>
        /// <param name="clusters"></param>
        private void ResetCenters(List<CellCluster> clusters)
        {
            foreach (CellCluster cluster in clusters)
            {
                cluster.Centroid.isClusterCenter = false;
            }
        }

        private bool TooFewCells(IVTrace strTrace){
            bool notValid = false;
            notValid |= strTrace.Vmp < mpptSpec.Vmin;
            //notValid |= strTrace.Imp < mpptSpec.Imin; //should this be here??
            notValid |= packSpec.Vmax / strTrace.Vmp > mpptSpec.MaxBR;
            return notValid;
        }

        private bool TooManyCells(IVTrace strTrace)
        {
            bool notValid = false; //should Imax be checked here?
            notValid |= strTrace.Voc > packSpec.Vmax;
            notValid |= strTrace.Voc > mpptSpec.VmaxIn;
            return notValid;
        }

        //private List<CellCluster> MakeClustersValid(List<CellCluster> clusters)
        //{
        //    int numClus = clusters.Count();
        //    bool tooManyCells;
        //    bool tooFewCells;
        //    for (int i = 0; i < numClus; i++)
        //    {
        //        var returnVal = ValidateClusterandGetPower(clusters[i].Cluster);
        //        tooFewCells = returnVal.Item1;
        //        tooManyCells = returnVal.Item2;
        //        if (tooFewCells & tooManyCells) continue; //can't help it //maybe it should be break?
        //        else if (tooFewCells)
        //        {
        //            List<int> clustersToSkip = new List<int>();
        //            AddCells(i, clusters, clustersToSkip);
        //        }
        //        else if (tooManyCells) TakeCells(i, clusters);
        //    }
        //    return clusters;
        //}

        /// <summary>
        /// Checkst to see if the list of cells can form a valid string
        /// and caluculates the total power through all time periods while it does
        /// <para>RETURN: Item1 = tooFewCells, Item2 = tooManyCells, Item3 = totalPower</para>
        /// </summary>
        //<returns>Tuple<bool tooFewCells, bool tooManyCells, doulbe totalPower> </returns>
        private Tuple<bool, bool, double> ValidateClusterandGetPower(List<ArraySpec.Cell> cells){
            bool tooManyCells = false;
            bool tooFewCells = false;
            double totalPower = 0;
            ArraySpec.CellString cellStr = new ArraySpec.CellString();
            cellStr.Cells = cells;
            //Debug.WriteLine("string as {0} cells", cellStr.Cells.Count());
            int numPeriods = cellStr.Cells[0].Insolation.Count();
            for (int j = 0; j < numPeriods; j++)
            {
                IVTrace clusterIV = StringSimulator.GetStringIV(cellStr, j, cTempLocal, byPassDiodeSpec, cellSpec);
                tooFewCells |= TooFewCells(clusterIV); 
                tooManyCells |= TooManyCells(clusterIV);
                totalPower += mpptSpec.PowerOut(clusterIV.Pmp, clusterIV.Vmp, packSpec.Vnom);
            }
            //Debug.WriteLine("TooFewCells: {0}, TooManyCells: {1}", tooFewCells, tooManyCells);
            return Tuple.Create(tooFewCells, tooManyCells, totalPower);
        }
        
        //private void TakeCells(int clusNum, List<CellCluster> clusters){
        //    //could either find the cell that is farthest from this center and add it to closest center
        //    //or find the closest center and add the cell cloest to it - should do this!!
        //    //otherwise you could take the one at the top and add it to the bottom.
        //    bool tooManyCells = true;
        //    double locScale = 1; //need to change this!
        //    int numClusters = clusters.Count();
        //    if (numClusters <= 1) return; //can't add cells from any where
        //    ArraySpec.Cell potential;
        //    ArraySpec.Cell clostestCenter = clusters[0].Centroid; //just for intializing
        //    double distance;
        //    double min;
        //    int clusToGiveTo = 0; //just or initializing
        //    List<int> clustersToSkip = new List<int>();
        //    clustersToSkip.Add(clusNum);

        //    while (tooManyCells & clustersToSkip.Count() < numClusters)
        //    { //either fixed the problem or tried all clusters
        //       // distance = 0;
        //        //min = Double.MaxValue;
        //        for (int i = 0; i < numClusters; i++){
        //            if (clustersToSkip.Contains(i)) continue; //skip to the next cluster

        //            //find closest to center
        //            potential = FindClosestMatchingCell(clusters[clusNum].Centroid, clusters[i].Cluster, locScale);
        //            distance = CalcSqrEuclidDist(potential, clusters[clusNum].Centroid, locScale);
        //            if (distance < min)
        //            {
        //                min = distance;
        //                closest = potential;
        //                clusToTakeFrom = i;
        //            }
        //        //for (int i = 0; i < numClusters; i++)
        //        Debug.Assert(min < Double.MaxValue, "All clusters got skipped in AddCells!");
        //        //check to see if removing cell makes cell still valid
        //        clusters[clusToTakeFrom].Cluster.Remove(closest);
        //        var returVal = ValidateClusterandGetPower(clusters[clusToTakeFrom].Cluster);
        //        if (returVal.Item1 | returVal.Item2)//cluster is no longer valid
        //        {
        //            clusters[clusToTakeFrom].Cluster.Add(closest); //add it back
        //            clustersToSkip.Add(clusToTakeFrom);//need to add cluster number ot a list
        //            //could make it recursive so that it hanles all the clusters...
        //        }
        //        else clusters[clusNum].Cluster.Add(closest);//add the cell to the need cluster               
        //        tooManyCells = ValidateClusterandGetPower(clusters[clusNum].Cluster).Item2;
        //    }
        //}


        /// <summary>
        /// Finds the closests cell to center of cluster numbered "clusNum" and adds adds to 
        /// that cluster. Checks to make sure that the cluster the cell was removed from remains valid.
        /// <para>Recursively goes through all clusters - either all cluters have too few cells or none do
        /// when function returns.</para>
        /// </summary>
        /// <param name="clusNum"></param>
        /// <param name="clusters"></param>
        //private void AddCells(int clusNum, List<CellCluster> clusters, List<int> clustersToSkip)
        //{
        //    bool tooFewCells = true;
        //    double locScale = 1; //need to change this!
        //    int numClusters = clusters.Count();
        //    if(numClusters <= 1) return; //can't add cells from any where
        //    ArraySpec.Cell potential;
        //    ArraySpec.Cell closest = clusters[0].Cluster[0]; //just for intializing
        //    double distance;
        //    double min;
        //    int clusToTakeFrom = 0; //just or initializing
        //    clustersToSkip.Add(clusNum);
        //    while(tooFewCells & clustersToSkip.Count()< numClusters){ //either fixed the problem or tried all clusters
        //        distance = 0;
        //        min = Double.MaxValue;
        //        for (int i = 0; i < numClusters; i++)
        //        {
        //            if (clustersToSkip.Contains(i)) continue; //skip to the next cluster
        //            //find closest to center
        //            potential = FindClosestMatchingCell(clusters[clusNum].Centroid, clusters[i].Cluster, locScale);
        //            distance = CalcSqrEuclidDist(potential, clusters[clusNum].Centroid, locScale);
        //            if (distance < min)
        //            {
        //                min = distance;
        //                closest = potential;
        //                clusToTakeFrom = i;
        //            }
        //        }
        //        Debug.Assert(min < Double.MaxValue, "All clusters got skipped in AddCells!");
        //        //check to see if removing cell makes cell still valid
        //        clusters[clusToTakeFrom].Cluster.Remove(closest);
        //        var returnVal = ValidateClusterandGetPower(clusters[clusToTakeFrom].Cluster);
        //        if (returnVal.Item1)//cluster doesn't have enough cells to give up
        //        {
        //            clusters[clusToTakeFrom].Centroid.isClusterCenter = false;
        //            clusters[clusToTakeFrom].Centroid = ReturnNewCenter(clusters[clusToTakeFrom].Cluster, locScale);
        //            clusters[clusToTakeFrom].Centroid.isClusterCenter = true;
        //            AddCells(clusToTakeFrom, clusters, clustersToSkip); //recurisve call
        //        }
        //        //when add cell returns - see if you can now take the cell from that cluster
        //        //the anding should make the return statement return immediately if there aren't too many cells
        //        if (returnVal.Item1 & ValidateClusterandGetPower(clusters[clusToTakeFrom].Cluster).Item1)
        //        {
        //            clusters[clusToTakeFrom].Cluster.Add(closest); //add it back
        //            clustersToSkip.Add(clusToTakeFrom);//need to add cluster number ot a list
        //            clusters[clusToTakeFrom].Centroid.isClusterCenter = false;
        //            clusters[clusToTakeFrom].Centroid = ReturnNewCenter(clusters[clusToTakeFrom].Cluster, locScale);
        //            clusters[clusToTakeFrom].Centroid.isClusterCenter = true;
        //            //could make it recursive so that it hanles all the clusters...
        //        }
        //        else
        //        {
        //            clusters[clusNum].Cluster.Add(closest);//add the cell to the need cluster
        //            //recalc centers
        //            clusters[clusNum].Centroid.isClusterCenter = false;
        //            clusters[clusToTakeFrom].Centroid.isClusterCenter = false;
        //            clusters[clusNum].Centroid = ReturnNewCenter(clusters[clusNum].Cluster, locScale);
        //            clusters[clusToTakeFrom].Centroid = ReturnNewCenter(clusters[clusToTakeFrom].Cluster, locScale);
        //            clusters[clusNum].Centroid.isClusterCenter = true;
        //            clusters[clusToTakeFrom].Centroid.isClusterCenter = true;
        //        }
        //        tooFewCells = ValidateClusterandGetPower(clusters[clusNum].Cluster).Item1;
        //    }
        //}


        private bool ValidateString(IVTrace strTrace)
        {
            bool valid = true;
            valid &= strTrace.Voc < packSpec.Vmax;
            valid &= strTrace.Isc < mpptSpec.ImaxIn;
            valid &= strTrace.Voc < mpptSpec.VmaxIn;
            valid &= strTrace.Vmp > mpptSpec.Vmin;
            valid &= strTrace.Imp > mpptSpec.Imin;
            valid &= packSpec.Vmax/strTrace.Vmp < mpptSpec.MaxBR; //should this be VOC?
            return valid;
        }

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
        private List<CellCluster> kMeansCluster(int k, List<ArraySpec.Cell> cells, double locScale, bool constrained = false)
        {
            List<CellCluster> clusters = IntializeKMeans(k, cells);

            //DisplayCenters(clusters);

            Debug.WriteLine("K-intialized with {0} clusters", clusters.Count());
            bool centersChange = true;
            int timesLooped = 0;
            while(centersChange){ //if the center changes, then cells must have changed clusters
                Debug.WriteLine("Has LOOPED {0} times", timesLooped);
                centersChange = false;

                if (constrained) clusters = AssignCellsToConstrainedClusters(clusters, locScale);
                else clusters = AssignCellsToClusters(clusters, locScale); //should change clusters

                //Debug.WriteLine("AfterAssign cluster[0] has {0} cells", clusters[0].Cluster.Count());
                int numCellsSwitched = 0;
                string cellNumbers = "";
                foreach(CellCluster cluster in clusters){
                    cellNumbers += cluster.Cluster.Count() + ", ";
                    ArraySpec.Cell newCenter = ReturnNewCenter(cluster.Cluster, locScale);
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
            //DisplayClusters(clusters);
            Debug.WriteLine("kmeans done!");
            return clusters;
        }
        private ArraySpec.Cell ReturnNewCenter(List<ArraySpec.Cell> cells, double locScale)
        {
            //MinCell(cluster.Cluster, 0, 1);
            ArraySpec.Cell newCenter = FindCenter(cells); //does not represent an actual cell
            return FindClosestMatchingCell(newCenter, cells, locScale);

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
        /// Populates each cluster with the minimum number of cells that are clostest to the Centroid and
        /// then adds the rest of the cells to the clostest centroid until the cluster reaches the max number
        /// of cells
        /// </summary>
        private List<CellCluster> AssignCellsToConstrainedClusters(List<CellCluster> clusters, double locScale)
        {
            //create a copy of of the clusters with the same centroids?
            //add new elements to the to that new one
            int k = clusters.Count();
            List<ArraySpec.Cell> cells = new List<ArraySpec.Cell>();
            List<CellCluster> newClusters = new List<CellCluster>();
            for (int i = 0; i < k; i++)
            {
                CellCluster clusterToAdd = new CellCluster();
                clusterToAdd.Centroid = clusters[i].Centroid;
                clusterToAdd.Cluster = new List<ArraySpec.Cell>();
                newClusters.Add(clusterToAdd);
                foreach(ArraySpec.Cell cell in clusters[i].Cluster){
                    cells.Add(cell);
                }
            }

            //make sure every cluster has at least 
            bool tooFewCells;
            ArraySpec.Cell closestCell;
            int clusterNum = 0;
            foreach (CellCluster centerCluster in newClusters)
            {
                Debug.WriteLine("Adding cells to cluster {0} which has {1} cells", clusterNum, centerCluster.Cluster.Count());
                int cellsAdded = 0;
                tooFewCells = true;
                while(tooFewCells && cells.Count() > 0){
                    //should I do min in the cluster here??????
                    closestCell = FindClosestMatchingCell(centerCluster.Centroid, cells, locScale);
                    centerCluster.Cluster.Add(closestCell);
                    cells.Remove(closestCell);
                    //see if there are still too few cells
                    if (centerCluster.Cluster.Count() >= MIN_CELLS_PER_CLUSTER) tooFewCells = false; //approximate first
                    if (!tooFewCells) tooFewCells = ValidateClusterandGetPower(centerCluster.Cluster).Item1; //find actual
                    cellsAdded++;
                    if (!tooFewCells) Debug.WriteLine("Enough cells in cluster {0} after adding {1}", clusterNum, cellsAdded);
                }

                clusterNum++;
            }
            //could also take cells away until there are no more
            //while (cells.Count() > 0)
            //{
            //}
            //loop through each cell and find the cluster that is clostest that is not full
            //could also add them in the order of which distance from cell to cluster is the least
                //this would take two loops through the data and what happens when cluster that is closest gets full?
            Debug.WriteLine("Assigning remaining {0} cells", cells.Count());
            double min;
            CellCluster clusterToAddTo = newClusters[0]; //assignment is temporary
            //List<int> fullClusters = new List<int>();
            //int cellsLeft = cells.Count();
            foreach (ArraySpec.Cell cell in cells)
            {
                min = Double.MaxValue;
                foreach (CellCluster centerCluster in newClusters)
                {
                    if (centerCluster.Cluster.Count() >= MAX_CELLS_PER_CLUSTER) continue;
                    double distance = CalcSqrEuclidDist(centerCluster.Centroid, cell, locScale);
                    if (distance < min)
                    {
                        //if (ValidateClusterandGetPower(centerCluster.Cluster).Item2) continue; //if it has too many cells skip it.
                        min = distance;
                        clusterToAddTo = centerCluster;
                    }
                }
                if (min < Double.MaxValue) {
                    //cellsLeft--;
                    clusterToAddTo.Cluster.Add(cell); //occurs when one cluster is not full
                }
                else break; //occurs if all clusters are full //not all cells could be assigned!!
                //what should I do if all the strings are full?????? Create a new cluster?
                //Debug.WriteLine("{0} cells to still add", cellsLeft);
            }
            return newClusters;
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

        
        //assumes things are in a strict grid
        /// <summary>
        /// Attempts to connect the cells together first in the x direction
        /// and then in the y. Is not full proof, but prevents it from being
        ///  random.
        /// </summary>
        /// <param name="cells"></param>
        /// <returns></returns>
        private List<ArraySpec.Cell> OrderCellsForLayout(List<ArraySpec.Cell> cells)
        {
            List<ArraySpec.Cell> orderedCells = new List<ArraySpec.Cell>();
            //orderedCells.Add(MinCell(cells,1,0));
            ArraySpec.Cell compareCell = new ArraySpec.Cell();
            ArraySpec.Cell cellToAdd;
            if(cells.Count()>0) cellToAdd = cells[0];
            else return cells;

            compareCell.Location = new PointF(0, 0);
            double distance = 0;
            double min;
            while (cells.Count() > 0)
            {
                min = Double.MaxValue;
                foreach (ArraySpec.Cell cell in cells)
                {
                    distance = CalcSqrEuclidDistLocation(cell, compareCell);
                    if (distance < min || ((distance < min*1.001) && (Xdistance(cell, compareCell) < distance)))
                    { //will connect x first
                        min = distance;
                        cellToAdd = cell;
                    }
                    //if (Xdistance(cell, compareCell) == 0)
                    //{
                    //    Debug.WriteLine("min: {0}", min);
                    //}
                    Debug.Assert(!CellsEqual(cellToAdd, compareCell), "cellToAdd it equal to compareCell");
                }
                orderedCells.Add(cellToAdd);
                cells.Remove(cellToAdd);
                compareCell = cellToAdd;
            }
            //orderedCells[0].isClusterCenter = true;
            return orderedCells;
        }

        private double Xdistance(ArraySpec.Cell cell1, ArraySpec.Cell cell2)
        {
            //return Math.Abs(cell1.Location.X - cell2.Location.X);
            double x1 = (double)cell1.Location.X;
            double x2 = (double)cell2.Location.X;
            double dist = Math.Pow(x1 - x2, 2);
            return Math.Sqrt(dist);
        }
        private float Ydistance(ArraySpec.Cell cell1, ArraySpec.Cell cell2)
        {
            return Math.Abs(cell1.Location.Y - cell2.Location.Y);
        }
    }
}
