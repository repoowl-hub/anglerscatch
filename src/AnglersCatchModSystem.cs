using System;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using HarmonyLib;

namespace AnglersCatch
{
    public class AnglersCatchModSystem : ModSystem
    {
        public static ICoreAPI ModApi { get; private set; }
        public static ICoreClientAPI ClientApi { get; private set; }

        private Harmony harmony;
        private ICoreAPI api;
        private ICoreServerAPI serverApi;

        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            this.api = api;
            ModApi = api;

            // Register behaviors and compatible item classes
            api.RegisterItemClass("ItemFishRaw", typeof(ItemFishRaw));
            api.RegisterItemClass("ItemFishTaxidermy", typeof(ItemFishTaxidermy));
            api.RegisterCollectibleBehaviorClass("FishItemBehavior", typeof(FishItemBehavior));
            api.RegisterEntityBehaviorClass("anglercatch:fishcapture", typeof(FishEntityBehavior));

            harmony = new Harmony("com.anglerscatch.fixes");
            harmony.PatchAll();
            DynamicBehaviorPatcher.PatchExternalModBehaviors(harmony, api);
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            base.StartClientSide(api);
            ClientApi = api;
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);
            this.serverApi = api;

            api.Event.OnEntityLoaded += OnEntityLoadedOrSpawned;
            api.Event.OnEntitySpawn += OnEntityLoadedOrSpawned;
            api.Event.OnEntityDeath += OnEntityDeath;

            api.Logger.Notification("[Angler's Catch] Server side initialized with entity lifecycle hooks.");
        }

        private void OnEntityLoadedOrSpawned(Entity entity)
        {
            if (entity == null) return;

            if (FishEntityBehavior.IsFishEntity(entity))
            {
                float existingSize = FishTransferManager.GetEntityFishSize(entity);
                if (existingSize <= 0f)
                {
                    // Check if this entity came from placing a caught live fish item
                    if (FishTransferManager.TryApplyPlacement(entity))
                    {
                        return;
                    }

                    // Wild spawned live fish in the world have NO size until caught!
                }
            }
        }

        private void OnEntityDeath(Entity entity, DamageSource damageSourceForDeath)
        {
            if (entity == null || !FishEntityBehavior.IsFishEntity(entity)) return;

            string species = FishTransferManager.GetEntitySpeciesCode(entity);
            float sizeCm = FishTransferManager.GetEntityFishSize(entity);
            string uid = entity.WatchedAttributes?.GetString("caughtByPlayerUid") ?? entity.Attributes?.GetString("caughtByPlayerUid", "");
            string name = entity.WatchedAttributes?.GetString("caughtByPlayerName") ?? entity.Attributes?.GetString("caughtByPlayerName", "");

            // Record player kill if struck by a player
            if (damageSourceForDeath?.SourceEntity is EntityPlayer entityPlayer && entityPlayer.Player is IServerPlayer player)
            {
                uid = player.PlayerUID;
                name = player.PlayerName;
            }

            if (sizeCm <= 0f)
            {
                bool isJuvenile = entity.Code?.Path?.EndsWith("juvenile") == true;
                sizeCm = FishItemBehavior.RollRandomSize(species, isJuvenile);
            }

            FishTransferManager.SetEntityFishAttributes(entity, sizeCm, species, uid, name);
            FishTransferManager.RecordDeath(entity);
        }

        public override void Dispose()
        {
            harmony?.UnpatchAll("com.anglerscatch.fixes");
            base.Dispose();
        }

        public override void AssetsFinalize(ICoreAPI api)
        {
            base.AssetsFinalize(api);
            FishSpeciesConfig.LoadFrom(api);

            int hookedItemsCount = 0;
            foreach (var item in api.World.Items)
            {
                if (item?.Code == null) continue;

                if (FishItemBehavior.IsFishCollectible(item))
                {
                    if (!item.HasBehavior<FishItemBehavior>())
                    {
                        var behavior = new FishItemBehavior(item);
                        item.CollectibleBehaviors = item.CollectibleBehaviors == null 
                            ? new CollectibleBehavior[] { behavior } 
                            : item.CollectibleBehaviors.Append(behavior).ToArray();
                    }

                    // Stack size 1 ensures unique catch size, weight, and catcher name per caught fish
                    item.MaxStackSize = 1;
                    hookedItemsCount++;
                }
            }

            api.Logger.Notification($"[Angler's Catch] Dynamically attached FishItemBehavior to {hookedItemsCount} fish items.");
        }
    }
}
