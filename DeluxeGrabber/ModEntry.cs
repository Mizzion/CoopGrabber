﻿using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Locations;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using System.Collections.Generic;

namespace DeluxeGrabber
{
    public class ModEntry : Mod {

        private ModConfig Config;
        private List<string> quality = new List<string>() { "basic", "silver", "gold", "3", "iridium" };
        private readonly int FARMING = 0;
        private readonly int FORAGING = 2;

        /// <summary>The mod entry point, called after the mod is first loaded.</summary>
        /// <param name="helper">Provides simplified APIs for writing mods.</param>
        public override void Entry(IModHelper helper) {

            Config = Helper.ReadConfig<ModConfig>();

            Helper.ConsoleCommands.Add("printLocation", "Print current map and tile location", PrintLocation);
            Helper.ConsoleCommands.Add("setForagerLocation", "Set current location as global grabber location", SetForagerLocation);

            helper.Events.GameLoop.DayStarted += OnDayStarted;
            helper.Events.World.ObjectListChanged += OnObjectListChanged;
        }

        /// <summary>Get an API that other mods can access. This is always called after <see cref="M:StardewModdingAPI.Mod.Entry(StardewModdingAPI.IModHelper)" />.</summary>
        public override object GetApi()
        {
            return new ModAPI(this.Config);
        }

        private void SetForagerLocation(string arg1, string[] arg2) {
            if (!Context.IsWorldReady) {
                return;
            }
            Config.GlobalForageMap = Game1.player.currentLocation.Name;
            Config.GlobalForageTileX = Game1.player.getTileX();
            Config.GlobalForageTileY = Game1.player.getTileY();
            Helper.WriteConfig(Config);
        }

        private void PrintLocation(string arg1, string[] arg2) {
            if (!Context.IsWorldReady) {
                return;
            }
            Monitor.Log($"Map: {Game1.player.currentLocation.Name}");
            Monitor.Log($"Tile: {Game1.player.getTileLocation()}");
        }

        /// <summary>Raised after objects are added or removed in a location.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event data.</param>
        private void OnObjectListChanged(object sender, ObjectListChangedEventArgs e) {
            if (!Config.DoHarvestTruffles) {
                return;
            }

            GameLocation foragerMap;
            foragerMap = Game1.getLocationFromName(Config.GlobalForageMap);
            if (foragerMap == null) {
                return;
            }

            foragerMap.Objects.TryGetValue(new Vector2(Config.GlobalForageTileX, Config.GlobalForageTileY), out Object grabber);

            if (grabber == null || !grabber.Name.Contains("Grabber")) {
                return;
            }

            
            System.Random random = new System.Random();
            foreach (KeyValuePair<Vector2, Object> pair in e.Added) {

                if (pair.Value.ParentSheetIndex != 430 || pair.Value.bigCraftable.Value) {
                    continue;
                }

                if ((grabber.heldObject.Value as Chest).items.Count >= 36) {
                    return;
                }

                Object obj = pair.Value;
                if (obj.Stack == 0) {
                    obj.Stack = 1;
                }

                if (!obj.isForage(null) && !IsGrabbableWorld(obj)) {
                    continue;
                }

                if (Game1.player.professions.Contains(16)) {
                    obj.Quality = 4;
                } else if (random.NextDouble() < Game1.player.ForagingLevel / 30.0) {
                    obj.Quality = 2;
                } else if (random.NextDouble() < Game1.player.ForagingLevel / 15.0) {
                    obj.Quality = 1;
                }

                if (Game1.player.professions.Contains(13)) {
                    while (random.NextDouble() < 0.2) {
                        obj.Stack += 1;
                    }
                }

                Monitor.Log($"Grabbing truffle: {obj.Stack}x{quality[obj.Quality]}", LogLevel.Trace);
                (grabber.heldObject.Value as Chest).addItem(obj);
                e.Location.Objects.Remove(pair.Key);

                if (Config.DoGainExperience) {
                    gainExperience(FORAGING, 7);
                }
            }

            if ((grabber.heldObject.Value as Chest).items.Count > 0) {
                grabber.showNextIndex.Value = true;
            }
        }

        /// <summary>Raised after the game begins a new day (including when the player loads a save).</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event data.</param>
        private void OnDayStarted(object sender, DayStartedEventArgs e) {

            AutograbBuildings();
            AutograbCrops();
            AutograbWorld();
        }

        private void AutograbBuildings() {
            Object grabber = null; // stores a reference to the autograbber
            List<Vector2> grabbables = new List<Vector2>(); // stores a list of all items which can be grabbed
            Dictionary<string, int> itemsAdded = new Dictionary<string, int>();
            GameLocation location;

            foreach (Building building in Game1.getFarm().buildings) {

                grabber = null;
                grabbables.Clear();
                itemsAdded.Clear();

                if (building.buildingType.Contains("Coop") || building.buildingType.Contains("Slime")) {

                    Monitor.Log($"Searching {building.buildingType} at <{building.tileX},{building.tileY}> for auto-grabber", LogLevel.Trace);

                    location = building.indoors.Value;
                    if (location != null) {

                        // populate list of objects which are grabbable, and find an autograbber
                        foreach (KeyValuePair<Vector2, Object> pair in location.Objects.Pairs) {
                            if (pair.Value.Name.Contains("Grabber")) {
                                Monitor.Log($"  Grabber found  at {pair.Key}", LogLevel.Trace);
                                grabber = pair.Value;
                            }
                            if (IsGrabbableCoop(pair.Value)) {
                                Monitor.Log($"    Found grabbable item at {pair.Key}: {pair.Value.Name}", LogLevel.Trace);
                                grabbables.Add(pair.Key);
                            }
                        }

                        if (grabber == null) {
                            Monitor.Log("  No grabber found", LogLevel.Trace);
                            continue;
                        }

                        // add items to grabber and remove from floor
                        bool full = false;
                        foreach (Vector2 tile in grabbables) {

                            if ((grabber.heldObject.Value as Chest).items.Count >= 36) {
                                Monitor.Log($"  Grabber is full", LogLevel.Trace);
                                full = true;
                                break;
                            }

                            if (location.objects[tile].Name.Contains("Slime Ball")) {
                                System.Random random = new System.Random((int)Game1.stats.daysPlayed + (int)Game1.uniqueIDForThisGame + (int)tile.X * 77 + (int)tile.Y * 777 + 2);
                                (grabber.heldObject.Value as Chest).addItem(new Object(766, random.Next(10, 21), false, -1, 0));
                                int i = 0;
                                while (random.NextDouble() < 0.33) {
                                    i++;
                                }
                                if (i > 0) {
                                    (grabber.heldObject.Value as Chest).addItem(new Object(557, i, false, -1, 0));
                                }
                            } else if ((grabber.heldObject.Value as Chest).addItem(location.Objects[tile]) != null) {
                                continue;
                            }
                            string name = location.Objects[tile].Name;
                            if (!itemsAdded.ContainsKey(name)) {
                                itemsAdded.Add(name, 1);
                            } else {
                                itemsAdded[name] += 1;
                            }
                            location.Objects.Remove(tile);
                            if (Config.DoGainExperience) {
                                gainExperience(FARMING, 5);
                            }
                        }

                        if (full) {
                            continue;
                        }

                        foreach (KeyValuePair<string, int> pair in itemsAdded) {
                            string plural = "";
                            if (pair.Value != 1) plural = "s";
                            Monitor.Log($"  Added {pair.Value} {pair.Key}{plural}", LogLevel.Trace);
                        }
                    }
                }

                // update sprite if grabber has items in it
                if (grabber != null && (grabber.heldObject.Value as Chest).items.Count > 0) {
                    grabber.showNextIndex.Value = true;
                }
            }
        }

        private void AutograbCrops() {

            if (!Config.DoHarvestCrops) {
                return;
            }

            int range = Config.GrabberRange;
            foreach (GameLocation location in Game1.locations) {
                foreach (KeyValuePair<Vector2, Object> pair in location.Objects.Pairs) {
                    if (pair.Value.Name.Contains("Grabber")) {
                        Object grabber = pair.Value;
                        if ((grabber.heldObject.Value == null) || (grabber.heldObject.Value as Chest).items.Count >= 36) {
                            continue;
                        }
                        bool full = (grabber.heldObject.Value as Chest).items.Count >= 36;
                        for (int x = (int)pair.Key.X - range; x < pair.Key.X + range + 1; x ++) {
                            for (int y = (int)pair.Key.Y - range; y < pair.Key.Y + range + 1 && !full; y++) {
                                Vector2 tile = new Vector2(x, y);
                                if (location.terrainFeatures.ContainsKey(tile) && location.terrainFeatures[tile] is HoeDirt dirt)
                                {
                                    Object harvest = GetHarvest(dirt, tile, location);
                                    if (harvest != null)
                                    {
                                        (grabber.heldObject.Value as Chest).addItem(harvest);
                                        if (Config.DoGainExperience)
                                        {
                                            gainExperience(FORAGING, 3);
                                        }
                                    }
                                }
                                else if (location.Objects.ContainsKey(tile) && location.Objects[tile] is IndoorPot pot)
                                {
                                    Object harvest = GetHarvest(pot.hoeDirt.Value, tile, location);
                                    if (harvest != null)
                                    {
                                        (grabber.heldObject.Value as Chest).addItem(harvest);
                                        if (Config.DoGainExperience)
                                        {
                                            gainExperience(FORAGING, 3);
                                        }
                                    }
                                    else if (pot.bush.Value != null) // tea bush - needs very special handling
                                    {
                                        // workaround for following problem:
                                        // - pot is initialized with (private) bushLoadDirty = true
                                        // - if updateWhenCurrentLocation is called while bushLoadDirty, it calls bush.loadSprite()
                                        // - Bush.loadSprite will reset tileSheetOffset to 1
                                        // summary: harvester will pick up tea leaves, then loadSprite will be called (after player enters
                                        // the location) and give player new batch to harvest manually on first day after game loads. We
                                        // call the update here to reset the dirty flag early, BEFORE letting TryHarvestBush do it's work
                                        pot.updateWhenCurrentLocation(Game1.currentGameTime, location);

                                        var harvests = TryHarvestBush(pot.bush.Value);
                                        foreach (var item in harvests)
                                        {
                                            if (!((grabber.heldObject.Value as Chest).items.Count >= 36))
                                            {
                                                (grabber.heldObject.Value as Chest).addItem(item);
                                            }
                                        }
                                    }
                                }
                                else if (location.terrainFeatures.ContainsKey(tile) && location.terrainFeatures[tile] is FruitTree fruitTree)
                                {
                                    Object fruit = GetFruit(fruitTree, tile, location);
                                    if (fruit != null)
                                    {
                                        (grabber.heldObject.Value as Chest).addItem(fruit);
                                        if (Config.DoGainExperience)
                                        {
                                            gainExperience(FORAGING, 3);
                                        }
                                    }
                                }
                                full = (grabber.heldObject.Value as Chest).items.Count >= 36;
                            }
                        }
                        if (grabber != null && (grabber.heldObject.Value as Chest).items.Count > 0) {
                            grabber.showNextIndex.Value = true;
                        }
                    }
                }
            }
        }

        private IEnumerable<Object> TryHarvestBush(Bush bush)
        {
            // adapted from Bush.shake(...)
            if (!bush.townBush.Value && bush.tileSheetOffset.Value == 1 && bush.inBloom(Game1.currentSeason, Game1.dayOfMonth))
            {
                int parentSheetIndex = -1;
                string currentSeason = Game1.currentSeason;
                if (!(currentSeason == "spring"))
                {
                    if (currentSeason == "fall")
                        parentSheetIndex = 410; // blackberry
                }
                else
                    parentSheetIndex = 296; // salmonberry
                if (bush.size.Value == 3)
                    parentSheetIndex = 815; // tea
                if (parentSheetIndex == -1)
                    return null;
                bush.tileSheetOffset.Value = 0;
                bush.setUpSourceRect();
                var random = new System.Random((int)bush.currentTileLocation.X + (int)bush.currentTileLocation.Y * 5000 + (int)Game1.uniqueIDForThisGame + (int)Game1.stats.DaysPlayed);
                if (bush.size.Value == 3)
                {
                    return new Object[] { new Object(815, 1, false, -1, 0) };
                    //int num = 1;
                    //for (int index = 0; index < num; ++index)
                    //{
                    //  Game1.createObjectDebris(815, (int)bush.currentTileLocation.X, (int)bush.currentTileLocation.Y, -1, 0, 1f, (GameLocation)null);
                    //}
                }
                else
                {
                    var harvest = new List<Object>();
                    int num = random.Next(1, 2) + Game1.player.ForagingLevel / 4;
                    for (int index = 0; index < num; ++index)
                    {
                        harvest.Add(new Object(parentSheetIndex, 1, false, -1, Game1.player.professions.Contains(16) ? 4 : 0));
                        //Game1.createItemDebris((Item)new StardewValley.Object(parentSheetIndex, 1, false, -1, Game1.player.professions.Contains(16) ? 4 : 0), Utility.PointToVector2(this.getBoundingBox().Center), Game1.random.Next(1, 4), (GameLocation)null, -1);
                    }
                    return harvest;
                }
            }
            return new Object[0];
        }

        private Object GetHarvest(HoeDirt dirt, Vector2 tile, GameLocation location) {

            Crop crop = dirt.crop;
            Object harvest;
            int stack = 0;

            if (crop is null) {
                return null;
            }

            if (!Config.DoHarvestFlowers) {
                switch(crop.indexOfHarvest.Value) {
                    case 421: return null; // sunflower
                    case 593: return null; // summer spangle
                    case 595: return null; // fairy rose
                    case 591: return null; // tulip
                    case 597: return null; // blue jazz
                    case 376: return null; // poppy
                }
            }

            if (crop != null && crop.currentPhase.Value >= crop.phaseDays.Count - 1 && (!crop.fullyGrown.Value || crop.dayOfCurrentPhase.Value <= 0)) {
                int num1 = 1;
                int num2 = 0;
                int num3 = 0;
                if (crop.indexOfHarvest.Value == 0)
                    return null;
                System.Random random = new System.Random((int)tile.X * 7 + (int)tile.Y * 11 + (int)Game1.stats.DaysPlayed + (int)Game1.uniqueIDForThisGame);
                switch ((dirt.fertilizer.Value)) {
                    case 368:
                        num3 = 1;
                        break;
                    case 369:
                        num3 = 2;
                        break;
                }
                double num4 = 0.2 * (Game1.player.FarmingLevel / 10.0) + 0.2 * num3 * ((Game1.player.FarmingLevel + 2.0) / 12.0) + 0.01;
                double num5 = System.Math.Min(0.75, num4 * 2.0);
                if (random.NextDouble() < num4)
                    num2 = 2;
                else if (random.NextDouble() < num5)
                    num2 = 1;
                if ((crop.minHarvest.Value) > 1 || (crop.maxHarvest.Value) > 1)
                    num1 = random.Next(crop.minHarvest.Value, System.Math.Min(crop.minHarvest.Value + 1, crop.maxHarvest.Value + 1 + Game1.player.FarmingLevel / (crop.maxHarvestIncreasePerFarmingLevel.Value > 0 ? crop.maxHarvestIncreasePerFarmingLevel.Value : 1)));
                if (crop.chanceForExtraCrops.Value > 0.0) {
                    while (random.NextDouble() < System.Math.Min(0.9, crop.chanceForExtraCrops.Value))
                        ++num1;
                }
                if (crop.harvestMethod.Value == 1) {
                    for (int i = 0; i < num1; i++) {
                        stack += 1;
                    }
                    if (crop.regrowAfterHarvest.Value != -1) {
                        crop.dayOfCurrentPhase.Value = crop.regrowAfterHarvest.Value;
                        crop.fullyGrown.Value = true;
                    } else {
                        dirt.crop = null;
                    }
                    return new Object(crop.indexOfHarvest.Value, stack, false, -1, num2);
                } else {
                    if (!crop.programColored.Value) {
                        harvest = new Object(crop.indexOfHarvest.Value, 1, false, -1, num2);
                    } else {
                        harvest = new ColoredObject(crop.indexOfHarvest.Value, 1, crop.tintColor.Value) { Quality = num2 };
                    }
                    if (crop.regrowAfterHarvest.Value != -1) {
                        crop.dayOfCurrentPhase.Value = crop.regrowAfterHarvest.Value;
                        crop.fullyGrown.Value = true;
                    } else {
                        dirt.crop = null;
                    }
                    return harvest;
                }
            }
            return null;
        }

        private Object GetFruit(FruitTree fruitTree, Vector2 tile, GameLocation location)
        {

            Object fruit = null;
            int quality = 0;

            if (fruitTree is null)
            {
                return null;
            }

            if (!Config.DoHarvestFruitTrees)
            {
                return null;
            }

            if (fruitTree.growthStage.Value >= 4)
            {
                if (fruitTree.fruitsOnTree.Value > 0)
                {
                    if (fruitTree.daysUntilMature.Value <= -112) { quality = 1; }
                    if (fruitTree.daysUntilMature.Value <= -224) { quality = 2; }
                    if (fruitTree.daysUntilMature.Value <= -336) { quality = 4; }
                    if (fruitTree.struckByLightningCountdown.Value > 0) { quality = 0; }

                    fruit = new Object(fruitTree.indexOfFruit.Value, fruitTree.fruitsOnTree.Value, false, -1, quality);
                    fruitTree.fruitsOnTree.Value = 0;
                    return fruit;
                }
            }

            return null;
        }

        private void AutograbWorld() {

            if (!Config.DoGlobalForage) {
                return;
            }

            List<Vector2> grabbables = new List<Vector2>(); // stores a list of all items which can be grabbed
            Dictionary<string, int> itemsAdded = new Dictionary<string, int>();
            System.Random random = new System.Random();
            GameLocation foragerMap;

            foragerMap = Game1.getLocationFromName(Config.GlobalForageMap);
            if (foragerMap == null) {
                Monitor.Log($"Invalid GlobalForageMap '{Config.GlobalForageMap}", LogLevel.Trace);
                return;
            }

            foragerMap.Objects.TryGetValue(new Vector2(Config.GlobalForageTileX, Config.GlobalForageTileY), out Object grabber);

            if (grabber == null || !grabber.Name.Contains("Grabber")) {
                Monitor.Log($"No auto-grabber at {Config.GlobalForageMap}: <{Config.GlobalForageTileX}, {Config.GlobalForageTileY}>", LogLevel.Trace);
                return;
            }

            foreach (GameLocation location in Game1.locations) {
                grabbables.Clear();
                itemsAdded.Clear();
                foreach (KeyValuePair<Vector2, Object> pair in location.Objects.Pairs) {
                    if (pair.Value.bigCraftable.Value) {
                        continue;
                    }
                    if (IsGrabbableWorld(pair.Value) || pair.Value.isForage(null)) {
                        grabbables.Add(pair.Key);
                    }
                }

                // Check for spring onions
                if (location.Name.Equals("Forest")) {
                    foreach (TerrainFeature feature in location.terrainFeatures.Values) {

                        if ((grabber.heldObject.Value as Chest).items.Count >= 36) {
                            Monitor.Log("Global grabber full", LogLevel.Info);
                            return;
                        }

                        if (feature is HoeDirt dirt) {
                            if (dirt.crop != null) {
                                if (dirt.crop.forageCrop.Value && dirt.crop.whichForageCrop.Value == 1) { // spring onion
                                    Object onion = new Object(399, 1, false, -1, 0);

                                    if (Game1.player.professions.Contains(16)) {
                                        onion.Quality = 4;
                                    } else if (random.NextDouble() < Game1.player.ForagingLevel / 30.0) {
                                        onion.Quality = 2;
                                    } else if (random.NextDouble() < Game1.player.ForagingLevel / 15.0) {
                                        onion.Quality = 1;
                                    }

                                    if (Game1.player.professions.Contains(13)) {
                                        while (random.NextDouble() < 0.2) {
                                            onion.Stack += 1;
                                        }
                                    }

                                    (grabber.heldObject.Value as Chest).addItem(onion);
                                    if (!itemsAdded.ContainsKey("Spring Onion")) {
                                        itemsAdded.Add("Spring Onion", 1);
                                    } else {
                                        itemsAdded["Spring Onion"] += 1;
                                    }

                                    dirt.crop = null;

                                    if (Config.DoGainExperience) {
                                        gainExperience(FORAGING, 3);
                                    }
                                }
                            }
                        }
                    }
                }

                // Check for berry (and tea) bushes
                string berryType;
                
                foreach (LargeTerrainFeature feature in location.largeTerrainFeatures) {

                    var bush = feature as Bush;
                    if (bush == null)
                    {
                        continue;
                    }
                    if (!bush.inBloom(Game1.currentSeason, Game1.dayOfMonth) || bush.tileSheetOffset.Value != 1)
                    {
                        continue;
                    }

                    if (Game1.currentSeason == "spring") {
                        berryType = "Salmon Berry";
                    } else if (Game1.currentSeason == "fall") {
                        berryType = "Blackberry";
                    } else {
                        berryType = "Tea Leaves";
                    }

                    if ((grabber.heldObject.Value as Chest).items.Count >= 36) {
                        Monitor.Log("Global grabber full", LogLevel.Info);
                        return;
                    }

                    var berries = TryHarvestBush(bush);

                    foreach (var berry in berries) {
                        Item item = (grabber.heldObject.Value as Chest).addItem(berry);
                        if (!itemsAdded.ContainsKey(berryType)) {
                            itemsAdded.Add(berryType, 1);
                        } else {
                            itemsAdded[berryType] += 1;
                        }
                    }
                }

                // add items to grabber and remove from floor
                foreach (Vector2 tile in grabbables) {

                    if ((grabber.heldObject.Value as Chest).items.Count >= 36) {
                        Monitor.Log("Global grabber full", LogLevel.Info);
                        return;
                    }

                    Object obj = location.Objects[tile];

                    if (Game1.player.professions.Contains(16)) {
                        obj.Quality = 4;
                    } else if (random.NextDouble() < Game1.player.ForagingLevel / 30.0) {
                        obj.Quality = 2;
                    } else if (random.NextDouble() < Game1.player.ForagingLevel / 15.0) {
                        obj.Quality = 1;
                    }

                    if (Game1.player.professions.Contains(13)) {
                        while (random.NextDouble() < 0.2) {
                            obj.Stack += 1;
                        }
                    }

                    Item item = (grabber.heldObject.Value as Chest).addItem(obj);
                    string name = location.Objects[tile].Name;
                    if (!itemsAdded.ContainsKey(name)) {
                        itemsAdded.Add(name, 1);
                    } else {
                        itemsAdded[name] += 1;
                    }
                    location.Objects.Remove(tile);

                    if (Config.DoGainExperience) {
                        gainExperience(FORAGING, 7);
                    }
                }

                // check farm cave for mushrooms
                if (Config.DoHarvestFarmCave)
                {
                    if (location is FarmCave)
                    {
                        foreach (Object obj in location.Objects.Values)
                        {

                            if ((grabber.heldObject.Value as Chest).items.Count >= 36)
                            {
                                Monitor.Log("Global grabber full", LogLevel.Info);
                                return;
                            }

                            if (obj.bigCraftable.Value && obj.ParentSheetIndex == 128)
                            {
                                if (obj.heldObject.Value != null)
                                {
                                    (grabber.heldObject.Value as Chest).addItem(obj.heldObject.Value);
                                    string name = grabber.heldObject.Value.Name;
                                    if (!itemsAdded.ContainsKey(name))
                                    {
                                        itemsAdded.Add(name, 1);
                                    }
                                    else
                                    {
                                        itemsAdded[name] += 1;
                                    }
                                    obj.heldObject.Value = null;
                                }
                            }
                        }
                    }
                }

                foreach (KeyValuePair<string, int> pair in itemsAdded) {
                    string plural = "";
                    if (pair.Value != 1) plural = "s";
                    Monitor.Log($"  {location} - found {pair.Value} {pair.Key}{plural}", LogLevel.Trace);
                }

                if ((grabber.heldObject.Value as Chest).items.Count > 0) {
                    grabber.showNextIndex.Value = true;
                }
            }
        }

        private bool IsGrabbableCoop(Object obj) {
            if (obj.bigCraftable.Value) {
                return obj.Name.Contains("Slime Ball");
            }

            if (obj.Name.Contains("Egg") || obj.Name.Contains("Wool") || obj.Name.Contains("Foot") || obj.Name.Contains("Feather")) {
                return true;
            }
            return false;
        }

        private bool IsGrabbableWorld(Object obj) {
            if (obj.bigCraftable.Value) {
                return false;
            }
            switch (obj.ParentSheetIndex) {
                case 16: // Wild Horseradish
                case 18: // Daffodil
                case 20: // Leek
                case 22: // Dandelion
                case 430: // Truffle
                case 399: // Spring Onion
                case 257: // Morel
                case 404: // Common Mushroom
                case 296: // Salmonberry
                case 396: // Spice Berry
                case 398: // Grape
                case 402: // Sweet Pea
                case 420: // Red Mushroom
                case 259: // Fiddlehead Fern
                case 406: // Wild Plum
                case 408: // Hazelnut
                case 410: // Blackberry
                case 281: // Chanterelle
                case 412: // Winter Root
                case 414: // Crystal Fruit
                case 416: // Snow Yam
                case 418: // Crocus
                case 283: // Holly
                case 392: // Nautilus Shell
                case 393: // Coral
                case 397: // Sea Urchin
                case 394: // Rainbow Shell
                case 372: // Clam
                case 718: // Cockle
                case 719: // Mussel
                case 723: // Oyster
                case 78: // Cave Carrot
                case 90: // Cactus Fruit
                case 88: // Coconut
                    return true;
            }
            return false;
        }

        private void gainExperience(int skill, int xp) {
            Game1.player.gainExperience(skill, xp);
        }
    }
}
