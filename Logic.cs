﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Logging;
using Math3D;
using System.ServiceModel.Dispatcher;
using System.Runtime.Serialization;

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
					return f(relative,other);
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

		private static Random random = new Random();

		private static JsonQueryStringConverter converter = new JsonQueryStringConverter();

		[DataContract]
		struct MoveToInstruction
		{
			[DataMemberAttribute]
			public uint gotoStack,
						gotoLayer;

		}


		static Grid.CellID currentTarget = Grid.CellID.Invalid;

		/// <summary>
		/// Central logic method. Invoked once per round to determine the next task for each technite.
		/// </summary>
		public static void Execute()
		{
			Out.Log(Significance.Common, "Execute()");

			foreach (var msg in Messages.All)
			{
				Out.Log(Significance.Unusual, "Got message from "+(msg.Item1 != null ? msg.Item1.ToString() : "(none)")+": "+msg.Item2);
				if (msg.Item1 == null)
				{
					try
					{
						MoveToInstruction target = (MoveToInstruction)converter.ConvertStringToValue(msg.Item2, typeof(MoveToInstruction));
						currentTarget = new Grid.CellID(target.gotoStack, (int)target.gotoLayer);
					}
					catch (Exception ex)
					{
						Out.Log(Significance.ClientFatal, ex.ToString());
					}
				}
			}




			if (currentTarget != Grid.CellID.Invalid)
			{
				if (currentTarget != Technite.Me.Location)
				{
					Queue<Grid.CellID> path = new Queue<Grid.CellID>();
					Dictionary<Grid.CellID, int> costs = new Dictionary<Grid.CellID, int>();
					Dictionary<Grid.CellID, Grid.CellID> prev = new Dictionary<Grid.CellID, Grid.CellID>();
					costs.Add(Technite.Me.Location, 0);
					//prev.Add(Technite.Me.Location, Technite.Me.Location);
					path.Enqueue(Technite.Me.Location);
					List<Grid.CellID> route = new List<Grid.CellID>();
					while (path.Count > 0)
					{
						Grid.CellID next = path.Dequeue();
						if (next == currentTarget)
						{
							Grid.CellID p;
//							route.Add(next);
							while (prev.TryGetValue(next, out p))
							{
								route.Add(next);
								next = p;
							}
							break;
						}
						int cost = costs[next];

						foreach (var n in next.GetNeighbors())
						{
							if (!Technite.EnoughSupportHere(n,true))
								continue;
							if (!Grid.IsClearWaterOrUndefined(n))
								continue;
							int cost2;
							if (costs.TryGetValue(n, out cost2))
							{
								if (cost2 <= cost + 1)
									continue;
								costs[n] = cost + 1;
								prev[n] = next;
							}
							else
							{
								costs.Add(n, cost + 1);
								prev.Add(n, next);
							}
							path.Enqueue(n);
						}
					}
					route.Reverse();

					if (route.Count > 0)
					{
						Grid.CellID cell = route[0];
						Grid.RelativeCell loc;
						if (Technite.Me.Location.FindRelative(cell, out loc))
						{
							Out.Log(Significance.Unusual, "Set target to " + cell);
							Technite.Me.SetNextTask(Technite.Task.Move, loc);
						}
						else
							Out.Log(Significance.Unusual, "Relativation failed from " + Technite.Me.Location+" to "+cell);
					}
					else
						Out.Log(Significance.Unusual, "Route not found to destination " + currentTarget);
				}

			}
			else
			{
				List<Grid.RelativeCell> candidates = new List<Grid.RelativeCell>();
				foreach (var loc in Technite.Me.Location.GetRelativeNeighbors())
				{
					Grid.CellID cell = Technite.Me.Location + loc;
					if (Grid.IsClearOrWater(cell) && Technite.EnoughSupportHere(cell))
					{
						candidates.Add(loc);
					}
					//				else
					//				Out.Log(Significance.Unusual, cell + " is " + Grid.World.GetCell(cell).content);

				}

				Out.Log(Significance.Unusual, "Got " + candidates.Count + " movement options from " + Technite.Me.Location);
				if (candidates.Count > 0)
				{
					int choice = random.Next(candidates.Count - 1);

					Grid.RelativeCell loc = candidates[choice];
					Grid.CellID cell = Technite.Me.Location + loc;
					Out.Log(Significance.Unusual, "Set target to " + cell);
					Technite.Me.SetNextTask(Technite.Task.Move, loc);
				}
			}
		}
	}
}
