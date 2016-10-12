using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Streams;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.DB;
using TShockAPI.Hooks;

namespace CommandRecipes
{
	[ApiVersion(1, 25)]
	public class CmdRec : TerrariaPlugin
	{
		public static List<string> cats = new List<string>();
		public static List<string> recs = new List<string>();
		public static RecipeDataManager Memory { get; private set; }
		public static RecConfig config { get; set; }
		public static string configDir { get { return Path.Combine(TShock.SavePath, "PluginConfigs"); } }
		public static string configPath { get { return Path.Combine(configDir, "AllRecipes.json"); } }
		public RecipeLog Log { get; set; }

		#region Info
		public override string Name
		{
			get { return "CommandRecipes"; }
		}

		public override string Author
		{
			get { return "aMoka & Enerdy"; }
		}

		public override string Description
		{
			get { return "Recipes through commands and chat."; }
		}

		public override Version Version
		{
			get { return Assembly.GetExecutingAssembly().GetName().Version; }
		}
		#endregion

		#region Initialize
		public override void Initialize()
		{
			PlayerHooks.PlayerPostLogin += OnLogin;
			PlayerHooks.PlayerLogout += OnLogout;
            GetDataHandlers.PlayerUpdate += OnPlayerUpdate;
            ServerApi.Hooks.GameInitialize.Register(this, OnInitialize);
			ServerApi.Hooks.NetGetData.Register(this, OnGetData);
		}
		#endregion

		#region Dispose
		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				PlayerHooks.PlayerPostLogin -= OnLogin;
				PlayerHooks.PlayerLogout -= OnLogout;
                GetDataHandlers.PlayerUpdate -= OnPlayerUpdate;
                ServerApi.Hooks.GameInitialize.Deregister(this, OnInitialize);
				ServerApi.Hooks.NetGetData.Deregister(this, OnGetData);

				Log.Dispose();
			}
		}
		#endregion

		public CmdRec(Main game)
			: base(game)
		{
			// Why did we need a lower order again?
			Order = -10;

			config = new RecConfig();
			Log = new RecipeLog();
		}

		#region OnInitialize
		void OnInitialize(EventArgs args)
		{
			Commands.ChatCommands.Add(new Command("cmdrec.player.craft", Craft, "craft")
			{
				HelpText = "Allows the player to craft items via command from config-defined recipes."
			});
			Commands.ChatCommands.Add(new Command("cmdrec.admin.reload", RecReload, "recrld")
			{
				HelpText = "Reloads AllRecipes.json"
			});

			Memory = new RecipeDataManager();
			//Utils.AddToPrefixes();
			Utils.SetUpConfig();
			Log.Initialize();
		}
		#endregion

		#region OnGetData
		void OnGetData(GetDataEventArgs args)
		{
			if (config.CraftFromInventory)
				return;

			if (args.MsgID == PacketTypes.ItemDrop)
			{
				if (args.Handled)
					return;

				using (var data = new MemoryStream(args.Msg.readBuffer, args.Index, args.Length))
				{
					Int16 id = data.ReadInt16();
					float posx = data.ReadSingle();
					float posy = data.ReadSingle();
					float velx = data.ReadSingle();
					float vely = data.ReadSingle();
					Int16 stacks = data.ReadInt16();
					int prefix = data.ReadByte();
					bool nodelay = data.ReadBoolean();
					Int16 netid = data.ReadInt16();

					Item item = new Item();
					item.SetDefaults(netid);

					if (id != 400)
						return;

					TSPlayer tsplayer = TShock.Players[args.Msg.whoAmI];
					RecipeData recData;
					if (tsplayer != null && tsplayer.Active && (recData = tsplayer.GetRecipeData()) != null && recData.activeRecipe != null)
					{
						List<Ingredient> fulfilledIngredient = new List<Ingredient>();
						foreach (Ingredient ing in recData.activeIngredients)
						{
							//ing.prefix == -1 means accepts any prefix
							if (ing.name == item.name && (ing.prefix == -1 || ing.prefix == prefix))
							{
								ing.stack -= stacks;

								if (ing.stack > 0)
								{
									tsplayer.SendInfoMessage("Drop another {0}.", Utils.FormatItem((Item)ing));
									if (recData.droppedItems.Exists(i => i.name == ing.name))
										recData.droppedItems.Find(i => i.name == ing.name).stack += stacks;
									else
										recData.droppedItems.Add(new RecItem(item.name, stacks, prefix));
									args.Handled = true;
									return;
								}
								else if (ing.stack < 0)
								{
									tsplayer.SendInfoMessage("Giving back {0}.", Utils.FormatItem((Item)ing));
									tsplayer.GiveItem(item.type, item.name, item.width, item.height, Math.Abs(ing.stack), prefix);
									if (recData.droppedItems.Exists(i => i.name == ing.name))
										recData.droppedItems.Find(i => i.name == ing.name).stack += (stacks + ing.stack);
									else
										recData.droppedItems.Add(new RecItem(item.name, stacks + ing.stack, prefix));
									foreach (Ingredient ingr in recData.activeIngredients)
										if ((ingr.group == 0 && ingr.name == ing.name) || (ingr.group != 0 && ingr.group == ing.group))
											fulfilledIngredient.Add(ingr);
									args.Handled = true;
								}
								else
								{
									tsplayer.SendInfoMessage("Dropped {0}.", Utils.FormatItem((Item)ing, stacks));
									if (recData.droppedItems.Exists(i => i.name == ing.name))
										recData.droppedItems.Find(i => i.name == ing.name).stack += stacks;
									else
										recData.droppedItems.Add(new RecItem(item.name, stacks, prefix));
									foreach (Ingredient ingr in recData.activeIngredients)
										if ((ingr.group == 0 && ingr.name == ing.name) || (ingr.group != 0 && ingr.group == ing.group))
											fulfilledIngredient.Add(ingr);
									args.Handled = true;
								}
							}
						}

						if (fulfilledIngredient.Count < 1)
							return;

						recData.activeIngredients.RemoveAll(i => fulfilledIngredient.Contains(i));

						foreach (Ingredient ing in recData.activeRecipe.ingredients)
						{
							if (ing.name == item.name && ing.prefix == -1)
								ing.prefix = prefix;
						}

						if (recData.activeIngredients.Count < 1)
						{
							List<Product> lDetPros = Utils.DetermineProducts(recData.activeRecipe.products);
							foreach (Product pro in lDetPros)
							{
								Item product = new Item();
								product.SetDefaults(pro.name);
								//itm.Prefix(-1) means at least a 25% chance to hit prefix = 0. if < -1, even chances. 
								product.Prefix(pro.prefix);
								pro.prefix = product.prefix;
								tsplayer.GiveItem(product.type, product.name, product.width, product.height, pro.stack, product.prefix);
								tsplayer.SendSuccessMessage("Received {0}.", Utils.FormatItem((Item)pro));
							}
							List<RecItem> prods = new List<RecItem>();
							lDetPros.ForEach(i => prods.Add(new RecItem(i.name, i.stack, i.prefix)));
							Log.Recipe(new LogRecipe(recData.activeRecipe.name, recData.droppedItems, prods), tsplayer.Name);
							// Commands :o (NullReferenceException-free :l)
							recData.activeRecipe.Clone().ExecuteCommands(tsplayer);
							recData.activeRecipe = null;
							recData.droppedItems.Clear();
							tsplayer.SendInfoMessage("Finished crafting.");
						}
					}
				}
			}
		}
		#endregion

		void OnLogin(PlayerPostLoginEventArgs args)
		{
			// Note to self: During login, TSPlayer.Active is set to False
			if (args.Player == null)
				return;

			if (Memory.Contains(args.Player.Name))
				args.Player.SetData(RecipeData.KEY, Memory.Load(args.Player.Name));

            args.Player.SetData<bool>("RecUseAny", config.UseAnyByDefault );
        }

		void OnLogout(PlayerLogoutEventArgs args)
		{
			if (args.Player == null || !args.Player.Active)
				return;

			RecipeData data = args.Player.GetRecipeData();
			if (data != null && data.activeRecipe != null)
				Memory.Save(args.Player);
		}

        private void OnPlayerUpdate(object sender, GetDataHandlers.PlayerUpdateEventArgs args)
        {
            if (!CmdRec.config.CraftFromInventory ) // None of this will work without crafting from inventory & SSC.
                return;

            TSPlayer p = TShock.Players[args.PlayerId];

            if ((args.Control & 32) == 32)
            {
                if (p.ContainsData("CmdRecUsing") && p.GetData<bool>("CmdRecUsing") == true) // Avoid repeatedly calling the below while holding down the button
                    return;
                p.SetData("CmdRecUsing", true);

                Item it = p.TPlayer.inventory[args.Item];

                // Various conditions where we do not check for recipes, i.e. items with actual uses.
                if (it.buffType > 0 || it.healLife > 0 || it.healMana > 0 || ( !p.GetData<bool>("RecUseAny") && (it.useStyle > 0 || it.consumable) ) )
                {
                    p.SetData<Recipe>("RecPrep", null);
                    return;
                }

                // If we've prepped a recipe, and this is the same item again, we craft it.
                Recipe savedrec = p.GetData<Recipe>("RecPrep");
                if (savedrec != null )
                {
                    if (p.GetData<Item>("RecItem") == it )
                    {
                        p.SetData<Recipe>("RecPrep", null);
                        p.CraftExecute(savedrec, config, Log, true, false);
                        return;
                    }
                    //p.SendInfoMessage("Recipe cleared.");
                }
                // Ensure the prep queue is empty otherwise.
                p.CraftCancel(false);
                p.SetData<Item>("RecItem", it);

                // Get all the recipes that can be made from that item. If there aren't any, exit.
                List<Recipe> recs = config.GetRecipes(itemname:it.name).FindAll(r => p.CraftRecipeHasPermission(r));
                if (recs.Count < 1)
                    return;

                // If we have all the ingredients for a recipe, just choose the first available one and prep it - skipping over single-item recipes.
                Recipe chosenrec = recs.Find(r => p.CraftRecipeHasIngredients(r, CmdRec.config, false) && !r.IsSingleItem );
                if (chosenrec != null)
                {
                    p.SetData<Recipe>("RecPrep", chosenrec);
                    p.CraftQueue(chosenrec, config, false);
                    p.SendInfoMessage("Queued the following; please use the item again to confirm:");
                    p.SendInfoMessage(chosenrec.name + ": " + chosenrec.RecipeDescription);
                    return;
                }

                // Otherwise, list out the recipes we found.
                bool headed = false;
                foreach( Recipe rec in recs.FindAll(r => !r.IsSingleItem) )
                {
                    if( !headed )
                    {
                        headed = true;
                        p.SendInfoMessage("The following recipes are available for that item:");
                    }
                    p.SendInfoMessage(rec.name + ": " + rec.RecipeDescription);
                }

                // THEN, check for single-item recipes.
                chosenrec = recs.Find(r => p.CraftRecipeHasIngredients(r, CmdRec.config, false) && r.IsSingleItem);
                if (chosenrec != null)
                {
                    p.SetData<Recipe>("RecPrep", chosenrec);
                    p.CraftQueue(chosenrec, config, false);
                    p.SendInfoMessage("");
                    p.SendInfoMessage("WARNING: Using the item again will consume " + chosenrec.ingredients.Find(ing => ing.name == it.name).stack + " to create: " + chosenrec.name);
                    return;
                }
            }
            else
            {
                p.SetData("CmdRecUsing", false);
            }

            return;
        }

        #region Commands
        #region Craft
        void Craft(CommandArgs args)
	    {
			//Item item;
			var recData = args.Player.GetRecipeData(true);
			if (args.Parameters.Count == 0)
			{
				args.Player.SendErrorMessage("Invalid syntax! Proper syntax: /craft <recipe>/-use/-quit/-cancel/-list/-allcats/-cat{0}>",
					(config.CraftFromInventory) ? "/-confirm" : "");
				return;
			}

			var subcmd = args.Parameters[0].ToLower();

			switch (subcmd)
			{
				case "-list":
					int page;
					if (!PaginationTools.TryParsePageNumber(args.Parameters, 1, args.Player, out page))
						return;

                    //List<string> allRec = new List<string>();

                    // Add any recipe that isn't invisible kappa
                    //foreach (Recipe rec in CmdRec.config.Recipes.FindAll(r => !r.invisible))
                    //    allRec.Add(rec.name);
                    string recname = "";
                    if( args.Parameters.Count > 1 )
                    {
                        recname = args.Parameters.Aggregate((i, j) => i + " " + j).Substring(6);
                    }
                    List<string> allRec = CmdRec.config.ListRecipes(namematch:recname);

                    PaginationTools.SendPage(args.Player, page, PaginationTools.BuildLinesFromTerms(allRec),
					new PaginationTools.Settings
					{
						HeaderFormat = "Recipes ({0}/{1}):",
						FooterFormat = "Type /craft -list {0} for more.",
						NothingToDisplayString = "No recipes found!"
					});
					return;
				case "-allcats":
					int pge;
					if (!PaginationTools.TryParsePageNumber(args.Parameters, 1, args.Player, out pge))
						return;

                    List<string> allCat = CmdRec.config.ListCategories();
                    //List<string> allCat = new List<string>();

                    // Another ditto from -list
                    //foreach (Recipe rec in CmdRec.config.Recipes.FindAll(r => !r.invisible))
                    //	rec.categories.ForEach(i =>
                    //	{
                    //		if (!allCat.Contains(i))
                    //			allCat.Add(i);
                    //	});

                    PaginationTools.SendPage(args.Player, 1, PaginationTools.BuildLinesFromTerms(allCat),
						new PaginationTools.Settings
						{
							HeaderFormat = "Recipe Categories ({0}/{1}):",
							FooterFormat = "Type /craft -cat {0} for more.",
							NothingToDisplayString = "There are currently no categories defined!"
						});
					return;
				case "-cat":
					if (args.Parameters.Count < 2)
					{
						args.Player.SendErrorMessage("Invalid category!");
						return;
					}

					args.Parameters.RemoveAt(0);
					string cat = string.Join(" ", args.Parameters);
					if (!cats.Contains(cat.ToLower()))
					{
						args.Player.SendErrorMessage("Invalid category!");
						return;
					}
					else
					{
                        List<string> catrec = CmdRec.config.ListRecipes(category:cat);
						//List<string> catrec = new List<string>();

						// Keep bringing them!
						//foreach (Recipe rec in config.Recipes.FindAll(r => !r.invisible))
						//{
						//	rec.categories.ForEach(i =>
						//	{
						//		if (cat.ToLower() == i.ToLower())
						//			catrec.Add(rec.name);
						//	});
						//}
						args.Player.SendInfoMessage("Recipes in this category:");
						args.Player.SendInfoMessage("{0}", String.Join(", ", catrec));
					}
					return;
				case "-quit":
                case "-cancel":
                    args.Player.CraftCancel();
                    return;
				case "-confirm":
                    args.Player.CraftConfirm(config, Log);
                    return;
                case "-use":
                    args.Player.SetData<bool>("RecUseAny", !args.Player.GetData<bool>("RecUseAny") );
                    if(args.Player.GetData<bool>("RecUseAny"))
                        args.Player.SendInfoMessage("Can now craft (almost) any recipe by using the ingredient. THIS WILL NOT PREVENT THE ITEM'S NORMAL EFFECTS. Use wisely!");
                    else
                        args.Player.SendInfoMessage("No longer attempting to craft with every item you use. Phew!");
                    return;
                default:
                    string str = string.Join(" ", args.Parameters);
                    args.Player.CraftQueue(str, config);
					break;
			}
		}
		#endregion

		#region RecConfigReload
		public static void RecReload(CommandArgs args)
		{
			Utils.SetUpConfig();
			args.Player.SendInfoMessage("Attempted to reload the config file");
		}
		#endregion
		#endregion
	}
}
