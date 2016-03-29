﻿using System;
using System.Collections.Generic;

namespace TechniteLogic
{
	public static class Messages
	{
		static List<Tuple<Technite, string>> messages = new List<Tuple<Technite, string>>();


		public static void Clear()
		{
			messages.Clear();
		}

		public static void Add(Technite sender, string msg)
		{
			messages.Add(new Tuple<Technite, string>(sender, msg));
		}

	}
}