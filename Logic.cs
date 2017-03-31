using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Logging;
using Math3D;

namespace TechniteLogic
{
    public class Logic
    {
        public static class Helper
        {
            static List<KeyValuePair<int, Grid.RelativeCell>> options = new List<KeyValuePair<int, Grid.RelativeCell>>();
            static Random random = new Random();

            public const int NotAChoice = 0;

            /// <summary>
            /// Evaluates all possible neighbor cells. The return values of <paramref name="f"/> are used as probability multipliers 
            /// to chose a random a option.
            /// Currently not thread-safe
            /// </summary>
            /// <param name="location">Location to evaluate the neighborhood of</param>
            /// <param name="f">Evaluation function. Must return 0/NotAChoice if not applicable, and >1 otherwise. Higher values indicate higher probability</param>
            /// <returns>Chocen relative cell, or Grid.RelativeCell.Invalid if none was found.</returns>
            public static Grid.RelativeCell EvaluateChoices(Grid.CellID location, Func<Grid.RelativeCell, Grid.CellID, int> f)
            {
                options.Clear();
                int total = 0;
                foreach (var n in location.GetRelativeNeighbors())
                {
                    Grid.CellID cellLocation = location + n;
                    int q = f(n, cellLocation);
                    if (q > 0)
                    {
                        total += q;
                        options.Add(new KeyValuePair<int, Grid.RelativeCell>(q, n));
                    }
                }
                if (total == 0)
                    return Grid.RelativeCell.Invalid;
                if (options.Count == 1)
                    return options[0].Value;
                int c = random.Next(total);
                foreach (var o in options)
                {
                    if (c <= o.Key)
                        return o.Value;
                    c -= o.Key;
                }
                Out.Log(Significance.ProgramFatal, "Logic error");
                return Grid.RelativeCell.Invalid;
            }


            /// <summary>
            /// Determines a feasible, possibly ideal neighbor technite target, based on a given evaluation function
            /// </summary>
            /// <param name="location"></param>
            /// <param name="f">Evaluation function. Must return 0/NotAChoice if not applicable, and >1 otherwise. Higher values indicate higher probability</param>
            /// <returns>Chocen relative cell, or Grid.RelativeCell.Invalid if none was found.</returns>
            public static Grid.RelativeCell EvaluateNeighborTechnites(Grid.CellID location, Func<Grid.RelativeCell, Technite, int> f)
            {
                return EvaluateChoices(location, (relative, cell) =>
                {
                    Grid.Content content = Grid.World.GetCell(cell).content;
                    if (content != Grid.Content.Technite)
                        return NotAChoice;
                    Technite other = Technite.Find(cell);
                    if (other == null)
                    {
                        Out.Log(Significance.Unusual, "Located neighboring technite in " + cell + ", but cannot find reference to class instance");
                        return NotAChoice;
                    }
                    return f(relative, other);
                }
                );
            }

            /// <summary>
            /// Determines a feasible, possibly ideal technite neighbor cell that is at the very least on the same height level.
            /// Higher and/or lit neighbor technites are favored
            /// </summary>
            /// <param name="location"></param>
            /// <returns></returns>
            public static Grid.RelativeCell GetLitOrUpperTechnite(Grid.CellID location)
            {
                return EvaluateNeighborTechnites(location, (relative, technite) =>
                {
                    int rs = 0;
                    if (technite.Status.Lit)
                        rs++;
                    rs += relative.HeightDelta;
                    return rs;
                });
            }

            /// <summary>
            /// Determines a feasible, possibly ideal technite neighbor cell that is at most on the same height level.
            /// Lower and/or unlit neighbor technites are favored
            /// </summary>
            /// <param name="location"></param>
            /// <returns></returns>
            public static Grid.RelativeCell GetUnlitOrLowerTechnite(Grid.CellID location)
            {
                return EvaluateNeighborTechnites(location, (relative, technite) =>
                {
                    int rs = 1;
                    if (technite.Status.Lit)
                        rs--;
                    rs -= relative.HeightDelta;
                    return rs;
                }
                );
            }

            /// <summary>
            /// Determines a food source in the neighborhood of the specified location
            /// </summary>
            /// <param name="location"></param>
            /// <returns></returns>
            public static Grid.RelativeCell GetFoodChoice(Grid.CellID location)
            {
                return EvaluateChoices(location, (relative, cell) =>
                {
                    Grid.Content content = Grid.World.GetCell(cell).content;
                    int yield = Technite.MatterYield[(int)content]; //zero is zero, no exceptions
                    if (Grid.World.GetCell(cell.BottomNeighbor).content != Grid.Content.Technite)
                        return yield;
                    return NotAChoice;
                }
                );
            }

            /// <summary>
            /// Determines a feasible neighborhood cell that can work as a replication destination.
            /// </summary>
            /// <param name="location"></param>
            /// <returns></returns>
            public static Grid.RelativeCell GetSplitTarget(Grid.CellID location)
            {
                return EvaluateChoices(location, (relative, cell) =>
                {
                    Grid.Content content = Grid.World.GetCell(cell).content;
                    int rs = 100;
                    if (content != Grid.Content.Clear && content != Grid.Content.Water)
                        rs -= 90;
                    if (Grid.World.GetCell(cell.TopNeighbor).content == Grid.Content.Technite)
                        return NotAChoice;  //probably a bad idea to split beneath technite

                    if (Technite.EnoughSupportHere(cell))
                        return relative.HeightDelta + rs;

                    return NotAChoice;
                }
                );
            }
        }




        private static Dictionary<Grid.CellID, List<Grid.CellID>> neighbours = new Dictionary<Grid.CellID, List<Grid.CellID>>();

        /// <summary>
        /// Bestimmung aller Pfad-Nachbarn von jeder Zelle in einem Pfad. Eine Zelle glit dann als Nachbar wenn sie die Nachfolgezelle im Pfad ist.
        /// Zu jeder Zelle werden in einer Liste alle Nachbarn gespeichert.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>

        public static void updateNeighbours(Grid.CellID[] path)
        {
            /*
             * Jede CellID (Key) bekommt eine Liste (Value) mit ihren Nachfolgern zugewiesen.
             */

            //Richtung wird beachtet.
            for (int i = 0; i < path.Length - 1; i++)
            {
                List<Grid.CellID> temp;
                //Key bereits vorhanden
                if (neighbours.ContainsKey(path[i]))
                {
                    temp = neighbours[path[i]];
                    bool alreadyIncluded = false;
                    foreach (Grid.CellID n in temp)
                    {
                        //Key-Value-Paar bereits enthalten (bei komb. Pfaden)
                        if (n.Equals(path[i + 1]))
                        {
                            alreadyIncluded = true;
                        }
                    }
                    if (!alreadyIncluded)
                    {
                        temp.Add(path[i + 1]);
                    }
                }
                //Key noch nicht vorhanden
                else
                {
                    temp = new List<Grid.CellID>();
                    temp.Add(path[i + 1]);
                    neighbours.Add(path[i], temp);
                }
            }
        }




        private static Random random = new Random();

        private static List<Grid.CellID> StartNodes = new List<Grid.CellID>();


        private static String logPfad1 = "..\\Logging\\debug.log";
        private static String logPfad2 = "..\\Logging\\metrik.log";

        private static readonly double normalizationValue = 0.456565681099892;
        private static readonly double dartthrowingRadius = 12;
        private static readonly double radius = 15;
        private static readonly double connectionRadius = 20;

        /// <summary>
        /// Bestimmung der Knotenpunkte für das Netz durch eine Iteration über alle Oberflächenzellen und einen gegebenen Radius.
        /// </summary>
        /// <param name="start"></param>
        /// <param name="maxRadius"></param>
        /// <returns></returns>

        public static void initNet(Grid.CellID start, double maxRadius)
        {
            bool setRadius = false;
            //No limited Radius if maxRadius is 0 or less
            if (maxRadius > 0)
            {
                setRadius = true;
            }
            List<Grid.CellID> candidates = new List<Grid.CellID>();
            Technite t = Technite.Find(start);

            shortestPathInitialize();

            StartNodes.Add(start);

            for (uint i = start.StackID; i < Grid.Graph.Nodes.Length; i++)
            {
                for (int j = 0; j < Grid.CellStack.LayersPerStack; j++)
                {
                    if (unvisited[i, j])
                    {
                        Grid.CellID cell = new Grid.CellID(i, j);

                        candidates.Add(cell);

                        bool isValid = true;
                        foreach (Grid.CellID n in StartNodes)
                        {

                            if (setRadius)
                            {
                                double distToStart = (cell.WorldPosition - start.WorldPosition).Length;
                                if (distToStart > maxRadius)
                                {
                                    isValid = false;
                                    break;
                                }
                            }

                            double dist = (cell.WorldPosition - n.WorldPosition).Length;
                            if (dist < radius)
                            {
                                isValid = false;
                                break;
                            }
                            else
                            {
                                isValid = true;
                            }
                        }

                        if (isValid)
                        {
                            StartNodes.Add(cell);
                            Grid.World.Mark(cell, new Technite.Color(0, 0, 255));
                        }
                    }
                }
            }

            for (uint i = start.StackID; i > 0; i--)
            {
                for (int j = 0; j < Grid.CellStack.LayersPerStack; j++)
                {
                    if (unvisited[i, j])
                    {
                        Grid.CellID cell = new Grid.CellID(i, j);

                        candidates.Add(cell);

                        bool isValid = true;
                        foreach (Grid.CellID n in StartNodes)
                        {

                            if (setRadius)
                            {
                                double distToStart = (cell.WorldPosition - start.WorldPosition).Length;
                                if (distToStart > maxRadius)
                                {
                                    isValid = false;
                                    break;
                                }
                            }

                            double dist = (cell.WorldPosition - n.WorldPosition).Length;
                            if (dist < radius)
                            {
                                isValid = false;
                                break;
                            }
                            else
                            {
                                isValid = true;
                            }
                        }

                        if (isValid)
                        {
                            StartNodes.Add(cell);
                            Grid.World.Mark(cell, new Technite.Color(0, 0, 255));
                        }
                    }
                }
            }


            t.PrintDebugMessage("Nodes: " + (StartNodes.Count - 1));

            //Fehlerausgleichs-Algorithmen
            addNodesIfNeeded1(candidates);
            //addNodesIfNeeded2(candidates);


        }

        //Dartthrowing
        static int totalTechnitesBuilt = 0;
        static int netPoints = 0;
        static int netPaths = 0;
        static int combinedPaths = 0;
        static Double combinedPathYield = 0;
        static int totalPaths = 0;

        /// <summary>
        /// Bestimmung der Knotenpunkte für das Netz mittels Dartthrowing und einem gegebenen Radius.
        /// </summary>
        /// <param name="start"></param>
        /// <param name="maxRadius"></param>
        /// <returns></returns>

        public static void initNetDartThrowing(Grid.CellID start, double maxRadius)
        {
            bool setRadius = false;
            if (maxRadius > 0)
            {
                setRadius = true;
            }
            List<Grid.CellID> candidates = new List<Grid.CellID>();
            Technite t = Technite.Find(start);

            shortestPathInitialize();

            netPoints = 1;
            t.PrintDebugMessage("Dartthrowing started");
            for (uint i = start.StackID; i < Grid.Graph.Nodes.Length; i++)
            {
                for (int j = 0; j < Grid.CellStack.LayersPerStack; j++)
                {
                    Grid.CellID cell = new Grid.CellID(i, j);
                    if (unvisited[i, j] && (Grid.World.GetCell(cell).content == Grid.Content.Clear || Grid.World.GetCell(cell).content == Grid.Content.Technite || Grid.World.GetCell(cell).content == Grid.Content.Water) && (Grid.IsSolid(cell.BottomNeighbor, true)))
                    {
                        candidates.Add(cell);
                    }
                }
            }

            for (uint i = start.StackID; i > 0; i--)
            {
                for (int j = 0; j < Grid.CellStack.LayersPerStack; j++)
                {
                    Grid.CellID cell = new Grid.CellID(i, j);
                    if (unvisited[i, j] && (Grid.World.GetCell(cell).content == Grid.Content.Clear || Grid.World.GetCell(cell).content == Grid.Content.Technite || Grid.World.GetCell(cell).content == Grid.Content.Water) && (Grid.IsSolid(cell.BottomNeighbor, true)))
                    {
                        candidates.Add(cell);
                    }
                }
            }


            List<Grid.CellID>[] results = new List<Grid.CellID>[100];
            int bestResultIndex = 0;
            int bestResult = 0;

            //Berechne 100 Resultate und bestimme das Beste
            for (int i = 0; i < results.Length; i++)
            {
                results[i] = new List<Grid.CellID>();
                results[i].Add(start);
                int unsuccessfullTrys = 0;

                Random rnd = new Random();
                while (unsuccessfullTrys < 500)
                {
                    Boolean isValid = true;
                    int rndIndex = rnd.Next(0, candidates.Count);
                    foreach (Grid.CellID n in results[i])
                    {

                        if (setRadius)
                        {
                            double distToStart = (candidates[rndIndex].WorldPosition - start.WorldPosition).Length;
                            if (distToStart > maxRadius)
                            {
                                isValid = false;
                                break;
                            }
                        }

                        double dist = (candidates[rndIndex].WorldPosition - n.WorldPosition).Length;
                        if (dist < dartthrowingRadius)
                        {
                            isValid = false;
                            break;
                        }
                    }
                    if (isValid)
                    {
                        results[i].Add(candidates[rndIndex]);

                        unsuccessfullTrys = 0;
                    }
                    else
                    {
                        unsuccessfullTrys++;
                    }

                }
                int radiusSum = 0;
                foreach (Grid.CellID n in results[i])
                {
                    foreach (Grid.CellID m in results[i])
                    {
                        double dist = (n.WorldPosition - m.WorldPosition).Length;
                        if (dist < connectionRadius)
                        {
                            radiusSum++;
                        }
                    }

                }
                if (radiusSum > bestResult)
                {
                    bestResult = radiusSum;
                    bestResultIndex = i;
                }
            }


            StartNodes = results[bestResultIndex];
            foreach (Grid.CellID n in StartNodes)
            {
                Grid.World.Mark(n, new Technite.Color(0, 0, 255));
            }

            netPoints = StartNodes.Count;
            t.PrintDebugMessage("#Points: " + netPoints + " Score of Distribution: " + bestResult);

            //Fehlerausgleichs-Algorithmen
            addNodesIfNeeded1(candidates);
            //addNodesIfNeeded2(candidates);

        }

        /// <summary>
        /// Bestimmung weiterer Knotenpunkte als Fehlerausgleich der Netzpunkt-Algorithmen.
        /// Neue Knotenpunkte werden erstellt wenn ein Knotenpunkt unter 5 Nachbarn hat.
        /// </summary>
        /// <param name="candidates"></param>
        /// <returns></returns>

        public static void addNodesIfNeeded1(List<Grid.CellID> candidates)
        {
            if (StartNodes.Count != 0)
            {
                for (int i = 0; i < StartNodes.Count; i++)
                {
                    int neighbourNodeCount = 0;
                    foreach (Grid.CellID m in StartNodes)
                    {
                        if ((StartNodes[i].WorldPosition - m.WorldPosition).Length <= connectionRadius)
                        {
                            neighbourNodeCount++;
                        }
                    }
                    if (neighbourNodeCount <= 5)
                    {
                        Grid.CellID maxCell = new Grid.CellID(0, 0);
                        double maxDist = 0;
                        foreach (Grid.CellID m in candidates)
                        {
                            double tmpDist = 1;
                            if ((StartNodes[i].WorldPosition - m.WorldPosition).Length <= connectionRadius)
                            {
                                int w = 0;
                                foreach (Grid.CellID p in StartNodes)
                                {
                                    if ((p.WorldPosition - m.WorldPosition).Length <= connectionRadius)
                                    {
                                        tmpDist *= (p.WorldPosition - m.WorldPosition).Length;
                                        w++;
                                    }
                                }
                                tmpDist = Math.Pow(tmpDist, 1.0 / w);
                                if (tmpDist > maxDist)
                                {
                                    maxDist = tmpDist;
                                    maxCell = m;
                                }
                            }
                        }

                        if (maxCell.IsValid)
                        {
                            StartNodes.Add(maxCell);
                            Grid.World.Mark(maxCell, new Technite.Color(150, 150, 0));
                        }

                    }
                }
            }
        }



        private static List<Grid.CellID> potentialStartNodes = new List<Grid.CellID>();
        private static List<Grid.CellID> additionalStartNodes = new List<Grid.CellID>();

        /// <summary>
        /// Bestimmung weiterer Knotenpunkte als Fehlerausgleich der Netzpunkt-Algorithmen.
        /// Neue Knotenpunkte werden erstellt wenn es Zellen gibt die nicht im Radius der Knotenpunkte liegen.
        /// </summary>
        /// <param name="candidates"></param>
        /// <returns></returns>

        public static void addNodesIfNeeded2(List<Grid.CellID> candidates)
        {
            if (StartNodes.Count != 0)
            {
                int test = 0;
                foreach (Grid.CellID t in candidates)
                {
                    bool connected = false;
                    foreach (Grid.CellID nodes in StartNodes)
                    {
                        if ((t.WorldPosition - nodes.WorldPosition).Length < dartthrowingRadius - 1)
                        {
                            connected = true;
                            break;
                        }
                    }
                    test++;
                    if (!connected)
                    {

                        potentialStartNodes.Add(t);

                    }
                }
                using (System.IO.StreamWriter writetext = new System.IO.StreamWriter(logPfad1, true))
                {
                    writetext.WriteLine("Potential Nodes added: " + potentialStartNodes.Count);
                }

                while (potentialStartNodes.Count != 0)
                {

                    double maxDist = 0;
                    Grid.CellID maxCell = new Grid.CellID();
                    foreach (Grid.CellID pot in potentialStartNodes)
                    {
                        double tmpDist = 1;
                        int w = 0;
                        foreach (Grid.CellID p in StartNodes)
                        {
                            if ((p.WorldPosition - pot.WorldPosition).Length <= connectionRadius)
                            {
                                tmpDist *= (p.WorldPosition - pot.WorldPosition).Length;
                                w++;
                            }
                        }

                        tmpDist = Math.Pow(tmpDist, 1.0 / w);

                        if (tmpDist > maxDist)
                        {
                            maxDist = tmpDist;
                            maxCell = pot;
                        }
                    }
                    StartNodes.Add(maxCell);
                    potentialStartNodes.Remove(maxCell);
                    additionalStartNodes.Add(maxCell);
                    using (System.IO.StreamWriter writetext = new System.IO.StreamWriter(logPfad1, true))
                    {
                        writetext.WriteLine("Node: " + maxCell.ToString() + "added, had value: " + maxDist);
                    }

                    for (int i = potentialStartNodes.Count - 1; i >= 0; i--)
                    {
                        for (int j = 0; j < StartNodes.Count; j++)
                        {
                            using (System.IO.StreamWriter writetext = new System.IO.StreamWriter(logPfad1, true))
                                if ((potentialStartNodes[i].WorldPosition - StartNodes[j].WorldPosition).Length < dartthrowingRadius - 1)
                                {
                                    potentialStartNodes.Remove(potentialStartNodes[i]);
                                }
                        }

                    }

                    using (System.IO.StreamWriter writetext = new System.IO.StreamWriter(logPfad1, true))
                    {
                        writetext.WriteLine("Removed Nodes, new Count: " + potentialStartNodes.Count);
                    }
                }
            }
        }


        /// <summary>
        /// Bestimmung der Anfangszelle für einen Pfad, wenn das Zeil bekannt ist und von einem Netzpfad aus erreicht werden soll.
        /// </summary>
        /// <param name="target"></param>
        /// <returns></returns>

        public static void netToPoint(Grid.CellID target)
        {
            double minDist = double.MaxValue;
            Grid.CellID minCell = target;

            foreach (Grid.CellID[] n in allRoutes)
            {
                foreach (Grid.CellID cell in n)
                {
                    double dist = (cell.WorldPosition - target.WorldPosition).Length;
                    if (dist < minDist)
                    {
                        minDist = dist;
                        minCell = cell;
                    }
                }
            }

            if (target.Equals(minCell))
            {
                Technite t = Technite.Find(target);
                if (t != null)
                {
                    t.PrintDebugMessage("No Path Found");
                }
                return;
            }
            newRoute(minCell, target);
        }

        private static List<Grid.CellID[]> allRoutes = new List<Grid.CellID[]>();

        /// <summary>
        /// Bestimmung ob Pfade kombiniert verwendet werden für einen neuen Pfad.
        /// </summary>
        /// <param name="startCell"></param>
        /// <param name="endCell"></param>
        /// <returns></returns>

        public static void newRoute(Grid.CellID startCell, Grid.CellID endCell)
        {
            shortestPathInitialize();

            if (allRoutes.Count == 0)
            {
                Stack<Grid.CellID> path = shortestPathAStar(startCell, endCell);
                if (path != null)
                {
                    allRoutes.Add(path.ToArray());
                    updateNeighbours(path.ToArray());
                }
                else
                {
                    Technite t = Technite.Find(startCell);
                    if (t != null)
                    {
                        t.PrintDebugMessage("No Path Found");
                    }
                    return;
                }
            }
            else
            {
                Grid.CellID minStartCell = startCell;
                Grid.CellID minEndCell = endCell;

                int distanceStartEnd = (int)(endCell.WorldPosition - startCell.WorldPosition).Length;

                int shortestDistance = int.MaxValue;
                int stackIndex = 0;
                int startIndex = 0;
                int endIndex = 0;

                for (int j = 0; j < allRoutes.Count; j++)
                {
                    Grid.CellID[] n = allRoutes.ElementAt(j);

                    Grid.CellID tmpMinStart = startCell;
                    Grid.CellID tmpMinEnd = endCell;
                    int distMinStart = int.MaxValue;
                    int distMinEnd = int.MaxValue;
                    int tmpStackIndex = 0;
                    int tmpStartIndex = 0;
                    int tmpEndIndex = 0;

                    for (int i = 0; i < n.Length; i++)
                    {
                        int distanceStart = (int)(((n[i].WorldPosition - startCell.WorldPosition).Length) / convertRatio);

                        if (distanceStart < distMinStart)
                        {
                            distMinStart = distanceStart;
                            tmpMinStart = n[i];
                            tmpStackIndex = j;
                            tmpStartIndex = i;
                        }

                        int distanceEnd = (int)(((n[i].WorldPosition - endCell.WorldPosition).Length) / convertRatio);

                        if (distanceEnd < distMinEnd)
                        {
                            distMinEnd = distanceEnd;
                            tmpMinEnd = n[i];
                            tmpStackIndex = j;
                            tmpEndIndex = i;
                        }
                    }
                    if (distMinStart + distMinEnd < shortestDistance)
                    {
                        shortestDistance = distMinStart + distMinEnd;
                        minStartCell = tmpMinStart;
                        minEndCell = tmpMinEnd;
                        stackIndex = tmpStackIndex;
                        startIndex = tmpStartIndex;
                        endIndex = tmpEndIndex;
                    }
                }

                if (shortestDistance < distanceStartEnd)
                {
                    combinedPaths++;
                    combinedPathYield += (distanceStartEnd - shortestDistance);
                    Stack<Grid.CellID> pathStart = shortestPathAStar(startCell, minStartCell);
                    Stack<Grid.CellID> pathEnd = shortestPathAStar(minEndCell, endCell);
                    if (pathStart == null || pathEnd == null)
                    {
                        Technite t = Technite.Find(startCell);
                        if (t != null)
                        {
                            t.PrintDebugMessage("No Path Found");
                        }
                        return;
                    }
                    Grid.CellID[] midPath = allRoutes.ElementAt(stackIndex);
                    Grid.CellID[] firstPath = pathStart.ToArray();
                    Grid.CellID[] lastPath = pathEnd.ToArray();


                    Grid.CellID[] newPath = new Grid.CellID[firstPath.Length + Math.Abs(endIndex - startIndex) - 1 + lastPath.Length];
                    firstPath.CopyTo(newPath, 0);
                    if (startIndex <= endIndex)
                    {
                        Array.Copy(midPath, startIndex + 1, newPath, firstPath.Length, (endIndex - startIndex) - 1);
                    }
                    else
                    {
                        Grid.CellID[] tmpMidPath = new Grid.CellID[(startIndex - endIndex) - 1];
                        Array.Copy(midPath, endIndex + 1, tmpMidPath, 0, (startIndex - endIndex) - 1);
                        Array.Reverse(tmpMidPath);
                        tmpMidPath.CopyTo(newPath, firstPath.Length);
                    }
                    lastPath.CopyTo(newPath, firstPath.Length + Math.Abs(endIndex - startIndex) - 1);

                    allRoutes.Add(newPath);
                    updateNeighbours(newPath);
                }
                else
                {
                    Stack<Grid.CellID> path = shortestPathAStar(startCell, endCell);
                    if (path != null)
                    {
                        allRoutes.Add(path.ToArray());
                        updateNeighbours(path.ToArray());
                    }
                    else
                    {
                        Technite t = Technite.Find(startCell);
                        if (t != null)
                        {
                            t.PrintDebugMessage("No Path Found");
                        }
                        return;
                    }
                }
            }

            cellAncestor.Clear();
            costTotal.Clear();
            costToCurrentNode.Clear();
            totalPaths = allRoutes.Count;
        }


        private static bool[,] unvisited = new bool[Grid.Graph.Nodes.Length, Grid.CellStack.LayersPerStack];

        private static Dictionary<Grid.CellID, int> costTotal = new Dictionary<Grid.CellID, int>();
        private static Dictionary<Grid.CellID, Grid.CellID> cellAncestor = new Dictionary<Grid.CellID, Grid.CellID>();
        private static double convertRatio = 0;
        private static Dictionary<Grid.CellID, int> costToCurrentNode = new Dictionary<Grid.CellID, int>();



        /// <summary>
        /// Bestimmung der maximalen geometrischen Distanz zwischen zwei benachbarten Zellen.
        /// </summary>
        /// <returns></returns>

        private static void calculateConvertRatio()
        {
            double max = 0;

            for (uint i = 0; i < Grid.Graph.Nodes.Length; i++)
            {
                for (int j = 0; j < Grid.CellStack.LayersPerStack; j++)
                {
                    Grid.CellID tmp = new Grid.CellID(i, j);
                    foreach (Grid.CellID n in tmp.GetNeighbors())
                    {
                        double dist = (n.WorldPosition - tmp.WorldPosition).Length;
                        if (dist > max)
                        {
                            max = dist;
                        }
                    }
                }
            }
            convertRatio = max;
        }

        private static List<Grid.CellID> Openlist = new List<Grid.CellID>();

        /// <summary>
        /// A-Stern-Algorithmus zur Berechnung des kürzesten Pfades zwischen zwei Punkten.
        /// </summary>
        /// <param name="startCell"></param>
        /// <param name="endCell"></param>
        /// <returns></returns>

        public static Stack<Grid.CellID> shortestPathAStar(Grid.CellID startCell, Grid.CellID endCell)
        {
            Grid.CellID minCell = startCell;
            costTotal[startCell] = 0;
            costToCurrentNode[startCell] = 0;
            Openlist.Clear();
            Openlist.Add(minCell);
            while (Openlist.Count != 0)
            {
                double tmpDist = uint.MaxValue;

                Grid.CellID tmpCell = startCell;
                foreach (Grid.CellID n in Openlist)
                {
                    if (unvisited[n.StackID, n.Layer] == true && costTotal[n] < tmpDist)
                    {
                        tmpCell = n;
                        tmpDist = costTotal[tmpCell];
                    }
                }
                minCell = tmpCell;

                if (minCell.Equals(endCell))
                {
                    Stack<Grid.CellID> path = createPath(endCell, startCell);
                    return path;
                }

                unvisited[minCell.StackID, minCell.Layer] = false;

                foreach (Grid.CellID n in minCell.GetNeighbors())
                {
                    if (unvisited[n.StackID, n.Layer] == false)
                    {
                        continue;
                    }

                    int currentDist = costToCurrentNode[minCell] + 1;
                    if (unvisited[n.StackID, n.Layer] == true && currentDist >= costToCurrentNode[n])
                    {
                        continue;
                    }
                    cellAncestor[n] = minCell;
                    costToCurrentNode[n] = currentDist;

                    double f = (((n.WorldPosition - endCell.WorldPosition).Length) / convertRatio) + currentDist;
                    costTotal[n] = (int)Math.Floor(f);

                    if (!Openlist.Contains(n))
                    {
                        Openlist.Add(n);
                    }
                }
                Openlist.Remove(minCell);
            }
            return null;
        }


        /// <summary>
        /// Bestimmung der Oberflächenzellen der Welt und Initialisierung der Parameter für die Wegfindungsalgorithmen.
        /// </summary>
        /// <returns></returns>

        public static void shortestPathInitialize()
        {
            cellAncestor.Clear();
            costTotal.Clear();
            costToCurrentNode.Clear();
            for (uint i = 0; i < Grid.Graph.Nodes.Length; i++)
            {
                for (int j = 0; j < Grid.CellStack.LayersPerStack; j++)
                {
                    Grid.CellID cell = new Grid.CellID(i, j);
                    bool solidHorizontalNeighbors = false;
                    foreach (Grid.CellID n in cell.GetHorizontalNeighbors())
                    {
                        if (Grid.IsSolid(n, true))
                        {
                            solidHorizontalNeighbors = true;
                            break;
                        }
                    }
                    if ((Grid.World.GetCell(cell).content == Grid.Content.Clear || Grid.World.GetCell(cell).content == Grid.Content.Technite || Grid.World.GetCell(cell).content == Grid.Content.Water) && (Grid.IsSolid(cell.BottomNeighbor, true) || solidHorizontalNeighbors))
                    {
                        cellAncestor.Add(cell, cell);
                        costTotal.Add(cell, int.MaxValue);
                        costToCurrentNode.Add(cell, int.MaxValue);
                        unvisited[i, j] = true;
                    }
                    else
                    {
                        unvisited[i, j] = false;
                    }
                }
            }
        }

        /// <summary>
        /// Bestimmung eines Weges über die Vorfahrinformationen der Zellen.
        /// </summary>
        /// <param name="end"></param>
        /// <param name="start"></param>
        /// <returns></returns>

        public static Stack<Grid.CellID> createPath(Grid.CellID end, Grid.CellID start)
        {
            Stack<Grid.CellID> path = new Stack<Grid.CellID>();
            Grid.CellID current = end;
            while (!current.Equals(cellAncestor[current]))
            {
                path.Push(current);
                current = cellAncestor[current];
            }
            path.Push(current);
            return path;
        }



        private static bool[,] usedTechnite = new bool[Grid.Graph.Nodes.Length, Grid.CellStack.LayersPerStack];
        private static Stack<Grid.CellID> spawnedTechnites = new Stack<Grid.CellID>();

        private static bool firstRun = true;
        private static Boolean netStarted = false;

        /// <summary>
        /// Central logic method. Invoked once per round to determine the next task for each technite.
        /// </summary>

        public static void ProcessTechnites()
        {
            if (firstRun)
            {
                calculateConvertRatio();
                firstRun = false;
            }

            foreach (Grid.CellID n in additionalStartNodes)
            {
                Grid.World.Mark(n, new Technite.Color(255, 0, 0));
            }

            Out.Log(Significance.Common, "ProcessTechnites()");

            foreach (Technite t in Technite.All)
            {
                if (t.Status.TTL <= 1)
                {
                    t.PrintDebugMessage("I'll be dead next round");
                    t.SetCustomColor(new Technite.Color(255, 0, 0));
                }
                else
                {
                    float r0 = Grid.CellStack.HeightPerLayer * 2f;
                    float r1 = r0 + Grid.CellStack.HeightPerLayer * 2f;
                    float r02 = r0 * r0,
                            r12 = r1 * r1;
                    int atRange = 2;
                    foreach (var obj in Objects.AllGameObjects)
                    {
                        float d2 = Vec.QuadraticDistance(obj.ID.Location.WorldPosition, t.Location.WorldPosition);
                        if (d2 < r12)
                        {
                            atRange = 1;
                            if (d2 < r02)
                            {
                                atRange = 0;
                                break;
                            }
                        }
                    }
                }

                int count = 0;
                foreach (Grid.CellID n in t.Location.GetNeighbors())
                {
                    if (Grid.World.GetCell(n).content == Grid.Content.Technite)
                    {
                        count = 1;
                        break;
                    }
                }
                if (count == 0 && t != null && usedTechnite[t.Location.StackID, t.Location.Layer] == false)
                {
                    usedTechnite[t.Location.StackID, t.Location.Layer] = true;
                    spawnedTechnites.Push(t.Location);
                }

            }

            totalTechnitesBuilt = Technite.Count;

            if (spawnedTechnites.Count >= 2)
            {
                Grid.CellID tmpA = spawnedTechnites.Pop();
                Grid.CellID tmpB = spawnedTechnites.Pop();
                newRoute(tmpA, tmpB);
            }
            if (spawnedTechnites.Count == 1)
            {
                Grid.CellID tmpA = spawnedTechnites.Pop();
                if (!netStarted)
                {
                    //Aufruf der Netzpunkt-Algorithmen

                    initNet(tmpA, 0);
                    //initNetDartThrowing(tmpA, 0);
                    netStarted = true;
                }
                else
                {
                    netToPoint(tmpA);
                }
            }


            foreach (Grid.CellID[] n in allRoutes)
            {
                for (int i = 0; i < n.Length - 1; i++)
                {

                    if (Grid.World.GetCell(n[i + 1]).content != Grid.Content.Technite)
                    {
                        Grid.RelativeCell target = new Grid.RelativeCell(0, 0);
                        foreach (Grid.RelativeCell m in n[i].GetRelativeNeighbors())
                        {
                            if ((n[i] + m).IsValid)
                            {
                                if (n[i + 1].Equals(n[i] + m))
                                {
                                    target = m;
                                    break;
                                }
                            }
                        }

                        if (Grid.World.GetCell(n[i]).content == Grid.Content.Technite)
                        {
                            Technite t = Technite.Find(n[i]);
                            if (t != null && t.CanSplit)
                            {
                                t.SetNextTask(Technite.Task.GrowTo, target);
                            }
                            if (t != null && !t.CanSplit)
                            {
                                if (t.CurrentResources.Energy < 5)
                                {
                                    break;
                                }
                                Grid.RelativeCell targetFood = Helper.GetFoodChoice(t.Location);
                                if (targetFood != Grid.RelativeCell.Invalid)
                                {
                                    t.SetNextTask(Technite.Task.GnawAtSurroundingCell, targetFood);
                                }
                            }
                        }
                        break;
                    }
                    else
                    {
                        Boolean allNeighboursAreTechnites = true;
                        foreach (Grid.CellID c in neighbours[n[i]])
                        {
                            Technite t = Technite.Find(c);
                            if (t == null)
                            {
                                allNeighboursAreTechnites = false;
                                break;
                            }
                        }
                        if (!allNeighboursAreTechnites)
                        {
                            continue;
                        }

                        List<Grid.CellID> temp = neighbours[n[i]];

                        Grid.CellID neighbour = temp[0];

                        double minResources = Double.MaxValue;
                        foreach (Grid.CellID cell in temp)
                        {
                            Technite t = Technite.Find(cell);
                            if (t != null)
                            {
                                double resource = t.CurrentResources.Energy + t.CurrentResources.Matter;
                                if (minResources > resource)
                                {
                                    minResources = resource;
                                    neighbour = cell;
                                }
                            }
                        }

                        Grid.RelativeCell target = new Grid.RelativeCell(0, 0);
                        foreach (Grid.RelativeCell m in n[i].GetRelativeNeighbors())
                        {
                            if (neighbour.Equals(n[i] + m))
                            {
                                target = m;
                                break;
                            }
                        }
                        if (Grid.World.GetCell(n[i]).content == Grid.Content.Technite)
                        {
                            Technite t = Technite.Find(n[i]);
                            Technite neighbourTechnite = Technite.Find(neighbour);
                            if (t != null && t.CanSplit && (neighbourTechnite.CurrentResources.Energy != 255 || neighbourTechnite.CurrentResources.Matter != 255))
                            {
                                byte amount = 5;
                                if (neighbourTechnite.CurrentResources.Energy >= neighbourTechnite.CurrentResources.Matter)
                                {
                                    if (t.CurrentResources.Matter > 10)
                                    {
                                        amount = (byte)(t.CurrentResources.Matter / 2);
                                    }
                                    if (amount > 255 - neighbourTechnite.CurrentResources.Matter)
                                    {
                                        amount = (byte)(255 - neighbourTechnite.CurrentResources.Matter);
                                    }
                                    t.SetNextTask(Technite.Task.TransferMatterTo, target, amount);
                                }
                                else
                                {
                                    if (t.CurrentResources.Energy > 10)
                                    {
                                        amount = (byte)(t.CurrentResources.Energy / 2);
                                    }
                                    if (amount > 255 - neighbourTechnite.CurrentResources.Energy)
                                    {
                                        amount = (byte)(255 - neighbourTechnite.CurrentResources.Energy);
                                    }
                                    t.SetNextTask(Technite.Task.TransferEnergyTo, target, amount);
                                }
                            }
                            else if (t != null && !t.CanSplit)
                            {
                                Grid.RelativeCell targetFood = Helper.GetFoodChoice(t.Location);
                                if (t.CurrentResources.Energy < 5)
                                {
                                    //break;
                                }
                                if (t.CurrentResources.Matter < 5)
                                {
                                    if (targetFood != Grid.RelativeCell.Invalid)
                                    {
                                        t.SetNextTask(Technite.Task.GnawAtSurroundingCell, targetFood);
                                    }
                                }
                            }
                        }

                    }

                }
            }


            //Rückläufiger Pfad
            foreach (Grid.CellID[] n in allRoutes)
            {
                for (int i = n.Length - 1; i > 0; i--)
                {
                    if (Grid.World.GetCell(n[i - 1]).content != Grid.Content.Technite)
                    {
                        Grid.RelativeCell target = new Grid.RelativeCell(0, 0);
                        foreach (Grid.RelativeCell m in n[i].GetRelativeNeighbors())
                        {
                            if ((n[i] + m).IsValid)
                            {
                                if (n[i - 1].Equals(n[i] + m))
                                {
                                    target = m;
                                    break;
                                }
                            }
                        }


                        if (Grid.World.GetCell(n[i]).content == Grid.Content.Technite)
                        {
                            Technite t = Technite.Find(n[i]);
                            if (t != null && t.CanSplit)
                            {
                                t.SetNextTask(Technite.Task.GrowTo, target);
                            }
                            if (t != null && !t.CanSplit)
                            {
                                if (t.CurrentResources.Energy < 5)
                                {
                                    break;
                                }
                                Grid.RelativeCell targetFood = Helper.GetFoodChoice(t.Location);
                                if (targetFood != Grid.RelativeCell.Invalid)
                                {
                                    t.SetNextTask(Technite.Task.GnawAtSurroundingCell, targetFood);
                                }
                            }
                        }
                        break;

                    }
                }
            }


            //Netzaufbau
            if (StartNodes.Count > 0)
            {
                for (int i = StartNodes.Count - 1; i >= 0; i--)
                {
                    Grid.World.Mark(StartNodes[i], new Technite.Color(0, 0, 255));
                    Technite t = Technite.Find(StartNodes[i]);
                    if (t != null)
                    {
                        foreach (Grid.CellID m in StartNodes)
                        {
                            double distance = (StartNodes[i].WorldPosition - m.WorldPosition).Length;
                            if (distance <= connectionRadius && distance != 0)
                            {

                                cellAncestor.Clear();
                                costTotal.Clear();
                                costToCurrentNode.Clear();
                                shortestPathInitialize();

                                Stack<Grid.CellID> path = shortestPathAStar(StartNodes[i], m);
                                if (path != null)
                                {
                                    allRoutes.Add(path.ToArray());
                                    updateNeighbours(path.ToArray());
                                    t.PrintDebugMessage("routing to " + m);
                                    netPaths++;
                                }
                            }
                        }
                        StartNodes.RemoveAt(i);
                        break;
                    }
                }
            }
        }
    }
}