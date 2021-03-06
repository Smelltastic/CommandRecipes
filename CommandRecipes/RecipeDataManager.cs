﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TShockAPI;

namespace CommandRecipes
{
	public class RecipeDataManager
	{
		private Dictionary<string, RecipeData> memory;

		public RecipeDataManager()
		{
			memory = new Dictionary<string, RecipeData>();
		}

		public bool Contains(string playerName)
		{
			return memory.ContainsKey(playerName);
		}

		public RecipeData Load(string playerName)
		{
			RecipeData data = memory[playerName]?.Clone();
			return data;
		}

		public bool Save(TSPlayer player)
		{
			if (player == null)
				return false;

			RecipeData data;
			return (data = player.GetRecipeData()) != null && SaveSlot(player.Name, data);
		}

		public bool SaveSlot(string playerName, RecipeData data)
		{
			if (Contains(playerName))
				return false;

			memory[playerName] = data.Clone();
			return true;
		}
	}
}
