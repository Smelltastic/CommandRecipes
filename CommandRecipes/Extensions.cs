using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TShockAPI;

namespace CommandRecipes
{
	public static class Extensions
	{
		public static RecipeData GetRecipeData(this TSPlayer player, bool createIfNotExists = false)
		{
			if (!player.ContainsData(RecipeData.KEY) && createIfNotExists)
			{
				player.SetData(RecipeData.KEY, new RecipeData());
			}
			return player.GetData<RecipeData>(RecipeData.KEY);
		}

		public static void AddToList(this Dictionary<string, List<Recipe>> dic, KeyValuePair<string, Recipe> pair)
		{
			if (dic.ContainsKey(pair.Key))
			{
				dic[pair.Key].Add(pair.Value);
			}
			else
			{
				dic.Add(pair.Key, new List<Recipe>() { pair.Value });
			}
		}

		// The old method didn't work for superadmin, sadly :(
		public static bool CheckPermissions(this TShockAPI.Group group, List<string> perms)
		{
			foreach (var perm in perms)
			{
				if (group.HasPermission(perm))
					return true;
			}
			return false;
		}

		public static Ingredient GetIngredient(this List<Ingredient> lItem, string name, int prefix)
		{
			foreach (var ing in lItem)
				if (ing.name == name && (ing.prefix == prefix || ing.prefix == -1))
					return ing;
			return null;
		}

		public static bool ContainsItem(this List<RecItem> lItem, string name, int prefix)
		{
			foreach (var item in lItem)
				if (item.name == name && (item.prefix == prefix || item.prefix == -1))
					return true;
			return false;
		}

		public static RecItem GetItem(this List<RecItem> lItem, string name, int prefix)
		{
			foreach (var item in lItem)
				if (item.name == name & (item.prefix == prefix || item.prefix == -1))
					return item;
			return null;
		}

        public static void CraftCancel(this TSPlayer player, bool alert = true)
        {
            var recData = player.GetRecipeData(true);
            Terraria.Item item;
            player.SetData<Recipe>("RecPrep", null);

            if (recData.activeRecipe == null)
            {
                if(alert)
                    player.SendErrorMessage("You aren't crafting anything!");
            }
            else
            {
                if(alert)
                    player.SendInfoMessage("Returning dropped items...");
                foreach (RecItem itm in recData.droppedItems)
                {
                    item = new Terraria.Item();
                    item.SetDefaults(itm.name);
                    player.GiveItem(item.type, itm.name, item.width, item.height, itm.stack, itm.prefix);
                    if(alert)
                        player.SendInfoMessage("Returned {0}.", Utils.FormatItem((Terraria.Item)itm));
                }
                recData.activeRecipe = null;
                recData.droppedItems.Clear();
                if(alert)
                    player.SendInfoMessage("Successfully quit crafting.");
            }
            return;
        }

        public static bool CraftExecute(this TSPlayer player, Recipe rec, RecConfig config, RecipeLog Log = null, bool alert = true, bool checkonly = false)
        {
            if (!config.CraftFromInventory)
            {
                if (alert)
                    player.SendErrorMessage("Crafting from inventory is disabled!");
            }

            Terraria.Item item;
            var recData = player.GetRecipeData(true);
            if( !rec.Equals (recData.activeRecipe) )
            {
                player.CraftQueue(rec, config, false, true);
            }

            int count = 0;
            Dictionary<int, bool> finishedGroup = new Dictionary<int, bool>();
            Dictionary<int, int> slots = new Dictionary<int, int>();
            int ingredientCount = recData.activeIngredients.Count;
            foreach (Ingredient ing in recData.activeIngredients)
            {
                if (!finishedGroup.ContainsKey(ing.group))
                {
                    finishedGroup.Add(ing.group, false);
                }
                else if (ing.group != 0)
                    ingredientCount--;
            }
            foreach (Ingredient ing in recData.activeIngredients)
            {
                if (ing.group == 0 || !finishedGroup[ing.group])
                {
                    Dictionary<int, RecItem> ingSlots = new Dictionary<int, RecItem>();
                    for (int i = 58; i >= 0; i--)
                    {
                        item = player.TPlayer.inventory[i];
                        if (ing.name == item.name && (ing.prefix == -1 || ing.prefix == item.prefix))
                        {
                            ingSlots.Add(i, new RecItem(item.name, item.stack, item.prefix));
                        }
                    }
                    if (ingSlots.Count == 0)
                        continue;

                    int totalStack = 0;
                    foreach (var key in ingSlots.Keys)
                        totalStack += ingSlots[key].stack;

                    if (totalStack >= ing.stack)
                    {
                        foreach (var key in ingSlots.Keys)
                            slots.Add(key, (ingSlots[key].stack < ing.stack) ? player.TPlayer.inventory[key].stack : ing.stack);
                        if (ing.group != 0)
                            finishedGroup[ing.group] = true;
                        count++;
                    }
                }
            }
            if (count < ingredientCount)
            {
                if (alert)
                    player.SendErrorMessage("Insufficient ingredients!");
                return false;
            }
            if (!player.InventorySlotAvailable)
            {
                if (alert)
                    player.SendErrorMessage("Insufficient inventory space!");
                return false;
            }
            if (checkonly)
                return true;
            foreach (var slot in slots)
            {
                item = player.TPlayer.inventory[slot.Key];
                var ing = recData.activeIngredients.GetIngredient(item.name, item.prefix);
                if (ing.stack > 0)
                {
                    int stack;
                    if (ing.stack < slot.Value)
                        stack = ing.stack;
                    else
                        stack = slot.Value;

                    item.stack -= stack;
                    ing.stack -= stack;
                    Terraria.NetMessage.SendData((int)PacketTypes.PlayerSlot, -1, -1, "", player.Index, slot.Key);
                    if (!recData.droppedItems.ContainsItem(item.name, item.prefix))
                        recData.droppedItems.Add(new RecItem(item.name, stack, item.prefix));
                    else
                        recData.droppedItems.GetItem(item.name, item.prefix).stack += slot.Value;
                }
            }
            List<Product> lDetPros = Utils.DetermineProducts(recData.activeRecipe.products);
            foreach (Product pro in lDetPros)
            {
                Terraria.Item product = new Terraria.Item();
                product.SetDefaults(pro.name);
                product.Prefix(pro.prefix);
                pro.prefix = product.prefix;
                player.GiveItem(product.type, product.name, product.width, product.height, pro.stack, product.prefix);
                if (alert)
                    player.SendSuccessMessage("Received {0}.", Utils.FormatItem((Terraria.Item)pro));
            }
            List<RecItem> prods = new List<RecItem>();
            lDetPros.ForEach(i => prods.Add(new RecItem(i.name, i.stack, i.prefix)));
            if (Log != null)
                Log.Recipe(new LogRecipe(recData.activeRecipe.name, recData.droppedItems, prods), player.Name);
            recData.activeRecipe.Clone().ExecuteCommands(player);
            recData.activeRecipe = null;
            recData.droppedItems.Clear();
            if (alert)
                player.SendInfoMessage("Finished crafting.");
            return true;
        }

        public static void CraftConfirm(this TSPlayer player, RecConfig config, RecipeLog Log = null, bool alert = true)
        {
            RecipeData recData = player.GetRecipeData(true);
            player.CraftExecute(recData.activeRecipe, config, Log, alert, false);
        }

        public static Boolean CraftRecipeHasPermission(this TSPlayer player, Recipe rec, bool alert = false)
        {
            if (!rec.permissions.Contains("") && !player.Group.CheckPermissions(rec.permissions))
            {
                if (alert)
                    player.SendErrorMessage("You do not have the required permission to craft the recipe: {0}!", rec.name);
                return false;
            }
            if (!Utils.CheckIfInRegion(player, rec.regions))
            {
                if (alert)
                    player.SendErrorMessage("You are not in a valid region to craft the recipe: {0}!", rec.name);
                return false;
            }

            return true;
        }

        public static bool CraftRecipeHasIngredients(this TSPlayer player, Recipe rec, RecConfig config, bool alert = false)
        {
            if (!config.CraftFromInventory)
            {
                if (alert)
                    player.SendErrorMessage("Crafting from inventory is not allowed on this server!");
                return false;
            }

            if (!player.CraftRecipeHasPermission(rec, alert))
                return false;

            return player.CraftExecute(rec, config, null, false, true);
       }

        public static void CraftQueue(this TSPlayer player, Recipe recipe, RecConfig config, bool alert = true, bool force = false)
        {
            RecipeData recData = player.GetRecipeData(true);
            if (recData.activeRecipe != null && !force)
            {
                if (alert)
                    player.SendErrorMessage("You must finish crafting or quit your current recipe!");
                return;
            }

            if (player.CraftRecipeHasPermission(recipe, true))
            {
                recData.activeIngredients = new List<Ingredient>(recipe.ingredients.Count);
                recipe.ingredients.ForEach(i =>
                {
                    recData.activeIngredients.Add(i.Clone());
                });
                recData.activeRecipe = recipe.Clone();
            }
            if (recData.activeRecipe != null)
            {
                List<string> inglist = Utils.ListIngredients(recData.activeRecipe.ingredients);
                //if (!args.Silent) // wtf? where is "silent" ever set or not set?
                if (alert)
                {
                    player.SendInfoMessage("The {0} recipe requires {1} to craft. {2}",
                      recData.activeRecipe.name,
                      (inglist.Count > 1) ? String.Join(", ", inglist.ToArray(), 0, inglist.Count - 1) + ", and " + inglist.LastOrDefault() : inglist[0],
                      (config.CraftFromInventory) ? "Type \"/craft -confirm\" to craft." : "Please drop all required items.");
                }
            }
        }

        public static void CraftQueue(this TSPlayer player, string recipe, RecConfig config, bool alert = true)
        {
            foreach (Recipe rec in config.Recipes)
            {
                if (recipe.ToLower() == rec.name.ToLower())
                {
                    player.CraftQueue(rec, config, alert);
                    return;
                }
            }
            if (alert)
                player.SendErrorMessage("Invalid recipe!");
            return;
        }
    }
}
