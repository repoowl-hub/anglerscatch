using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace AnglersCatch
{
    public class FishData
    {
        public float Size;
        public string Species;
        public string PlayerUid;
        public string PlayerName;
        public Vec3d Pos;
        public long Timestamp;
    }

    public static class FishTransferManager
    {
        private static readonly List<FishData> PendingPlacements = new List<FishData>();
        private static readonly List<FishData> PendingDeathDrops = new List<FishData>();
        private static readonly Dictionary<string, FishData> PendingCapturesByPlayer = new Dictionary<string, FishData>();
        private static readonly Dictionary<long, FishData> EntityPlacementsById = new Dictionary<long, FishData>();

        public static float ActiveFlayingMultiplier = 1f;
        public static string ActiveFlayingPlayer = null;
        public static BlockPos ActiveFlayingPos = null;

        public static float GetEntityFishSize(Entity entity)
        {
            if (entity == null) return 0f;
            float size = 0f;
            if (entity.WatchedAttributes != null)
            {
                size = entity.WatchedAttributes.GetFloat("fishSize", 0f);
            }
            if (size <= 0f && entity.Attributes != null)
            {
                size = entity.Attributes.GetFloat("fishSize", 0f);
            }
            return size;
        }

        public static string GetEntitySpeciesCode(Entity entity)
        {
            if (entity == null) return "trout-rainbow";
            string spec = null;
            if (entity.WatchedAttributes != null)
            {
                spec = entity.WatchedAttributes.GetString("speciesCode");
            }
            if (string.IsNullOrEmpty(spec) && entity.Attributes != null)
            {
                spec = entity.Attributes.GetString("speciesCode");
            }
            if (string.IsNullOrEmpty(spec))
            {
                spec = FishEntityBehavior.ExtractSpeciesFromCode(entity.Code?.Path);
            }
            return spec;
        }

        public static void SetEntityFishAttributes(Entity entity, float size, string species, string playerUid = null, string playerName = null)
        {
            if (entity == null || size <= 0f) return;
            if (string.IsNullOrEmpty(species)) species = FishEntityBehavior.ExtractSpeciesFromCode(entity.Code?.Path);

            if (entity.Attributes != null)
            {
                entity.Attributes.SetFloat("fishSize", size);
                entity.Attributes.SetString("speciesCode", species);
                if (!string.IsNullOrEmpty(playerUid)) entity.Attributes.SetString("caughtByPlayerUid", playerUid);
                if (!string.IsNullOrEmpty(playerName)) entity.Attributes.SetString("caughtByPlayerName", playerName);
            }

            if (entity.WatchedAttributes != null)
            {
                entity.WatchedAttributes.SetFloat("fishSize", size);
                entity.WatchedAttributes.SetString("speciesCode", species);
                if (!string.IsNullOrEmpty(playerUid)) entity.WatchedAttributes.SetString("caughtByPlayerUid", playerUid);
                if (!string.IsNullOrEmpty(playerName)) entity.WatchedAttributes.SetString("caughtByPlayerName", playerName);
                entity.WatchedAttributes.MarkPathDirty("fishSize");
                entity.WatchedAttributes.MarkPathDirty("speciesCode");
                if (!string.IsNullOrEmpty(playerUid)) entity.WatchedAttributes.MarkPathDirty("caughtByPlayerUid");
                if (!string.IsNullOrEmpty(playerName)) entity.WatchedAttributes.MarkPathDirty("caughtByPlayerName");
            }

            if (entity.Properties?.Client != null)
            {
                bool isJuvenile = entity.Code?.Path?.EndsWith("juvenile") == true;
                var range = FishSpeciesConfig.GetRange(species, isJuvenile);
                float ratio = Math.Clamp((size - range.MinSizeCm) / Math.Max(1f, (range.MaxSizeCm - range.MinSizeCm)), 0f, 1f);
                entity.Properties.Client.Size = 0.65f + (ratio * 0.90f);
            }
        }

        public static void TransferStackToEntity(ItemStack stack, Entity entity)
        {
            if (stack == null || entity == null) return;
            float size = stack.Attributes.GetFloat("fishSize", 0f);
            if (size <= 0f)
            {
                size = FishItemBehavior.EnsureFishAttributes(stack, entity.World);
            }
            string species = stack.Attributes.GetString("speciesCode", FishItemBehavior.GetSpeciesCode(stack));
            string uid = stack.Attributes.GetString("caughtByPlayerUid", "");
            string name = stack.Attributes.GetString("caughtByPlayerName", "");
            SetEntityFishAttributes(entity, size, species, uid, name);
        }

        public static void TransferEntityToStack(Entity entity, ItemStack stack)
        {
            if (entity == null || stack == null) return;
            float size = GetEntityFishSize(entity);
            string species = GetEntitySpeciesCode(entity);
            string uid = entity.WatchedAttributes?.GetString("caughtByPlayerUid") ?? entity.Attributes?.GetString("caughtByPlayerUid", "");
            string name = entity.WatchedAttributes?.GetString("caughtByPlayerName") ?? entity.Attributes?.GetString("caughtByPlayerName", "");

            if (size <= 0f)
            {
                bool isJuvenile = entity.Code?.Path?.EndsWith("juvenile") == true;
                size = FishItemBehavior.RollRandomSize(species, isJuvenile);
                SetEntityFishAttributes(entity, size, species, uid, name);
            }

            stack.Attributes.SetFloat("fishSize", size);
            stack.Attributes.SetString("speciesCode", species);
            if (!string.IsNullOrEmpty(uid)) stack.Attributes.SetString("caughtByPlayerUid", uid);
            if (!string.IsNullOrEmpty(name)) stack.Attributes.SetString("caughtByPlayerName", name);
        }

        public static void RecordPlacement(ItemStack stack, Vec3d pos, string playerUid = null)
        {
            if (stack == null || stack.Attributes == null) return;
            float size = stack.Attributes.GetFloat("fishSize", 0f);
            if (size <= 0f) return;

            long time = Environment.TickCount64;
            string uid = stack.Attributes.GetString("caughtByPlayerUid", playerUid ?? "");
            string name = stack.Attributes.GetString("caughtByPlayerName", "");
            string species = stack.Attributes.GetString("speciesCode", FishItemBehavior.GetSpeciesCode(stack));

            lock (PendingPlacements)
            {
                CleanExpired(PendingPlacements, time);
                PendingPlacements.Add(new FishData
                {
                    Size = size,
                    Species = species,
                    PlayerUid = uid,
                    PlayerName = name,
                    Pos = pos,
                    Timestamp = time
                });
            }

            if (!string.IsNullOrEmpty(playerUid))
            {
                lock (PendingCapturesByPlayer)
                {
                    PendingCapturesByPlayer[playerUid] = new FishData
                    {
                        Size = size,
                        Species = species,
                        PlayerUid = uid,
                        PlayerName = name,
                        Pos = pos,
                        Timestamp = time
                    };
                }
            }
        }

        public static bool TryApplyPlacement(Entity entity)
        {
            if (entity == null || entity.Pos == null) return false;
            Vec3d pos = entity.Pos.XYZ;
            long time = Environment.TickCount64;

            lock (PendingPlacements)
            {
                CleanExpired(PendingPlacements, time);
                for (int i = PendingPlacements.Count - 1; i >= 0; i--)
                {
                    var entry = PendingPlacements[i];
                    // Match within 16 blocks or recent timestamp
                    if (entry.Pos != null && entry.Pos.SquareDistanceTo(pos) <= 256.0)
                    {
                        SetEntityFishAttributes(entity, entry.Size, entry.Species, entry.PlayerUid, entry.PlayerName);
                        return true;
                    }
                }
            }
            return false;
        }

        public static void RecordDeath(Entity entity)
        {
            if (entity == null || entity.Pos == null) return;
            float size = GetEntityFishSize(entity);
            if (size <= 0f) return;

            string species = GetEntitySpeciesCode(entity);
            string uid = entity.WatchedAttributes?.GetString("caughtByPlayerUid") ?? entity.Attributes?.GetString("caughtByPlayerUid", "");
            string name = entity.WatchedAttributes?.GetString("caughtByPlayerName") ?? entity.Attributes?.GetString("caughtByPlayerName", "");
            long time = Environment.TickCount64;

            lock (PendingDeathDrops)
            {
                CleanExpired(PendingDeathDrops, time);
                PendingDeathDrops.Add(new FishData
                {
                    Size = size,
                    Species = species,
                    PlayerUid = uid,
                    PlayerName = name,
                    Pos = entity.Pos.XYZ,
                    Timestamp = time
                });
            }
        }

        public static bool TryApplyDeathDrop(ItemStack stack, Vec3d pos)
        {
            if (stack == null || pos == null) return false;
            long time = Environment.TickCount64;

            lock (PendingDeathDrops)
            {
                CleanExpired(PendingDeathDrops, time);
                for (int i = PendingDeathDrops.Count - 1; i >= 0; i--)
                {
                    var entry = PendingDeathDrops[i];
                    if (entry.Pos != null && entry.Pos.SquareDistanceTo(pos) <= 64.0) // within 8 blocks
                    {
                        stack.Attributes.SetFloat("fishSize", entry.Size);
                        stack.Attributes.SetString("speciesCode", entry.Species);
                        if (!string.IsNullOrEmpty(entry.PlayerUid)) stack.Attributes.SetString("caughtByPlayerUid", entry.PlayerUid);
                        if (!string.IsNullOrEmpty(entry.PlayerName)) stack.Attributes.SetString("caughtByPlayerName", entry.PlayerName);
                        return true;
                    }
                }
            }
            return false;
        }

        public static void RecordCapture(string playerUid, Entity entity)
        {
            if (entity == null) return;
            float size = GetEntityFishSize(entity);
            string species = GetEntitySpeciesCode(entity);
            string uid = entity.WatchedAttributes?.GetString("caughtByPlayerUid") ?? entity.Attributes?.GetString("caughtByPlayerUid", playerUid ?? "");
            string name = entity.WatchedAttributes?.GetString("caughtByPlayerName") ?? entity.Attributes?.GetString("caughtByPlayerName", "");

            if (size <= 0f)
            {
                bool isJuvenile = entity.Code?.Path?.EndsWith("juvenile") == true;
                size = FishItemBehavior.RollRandomSize(species, isJuvenile);
                SetEntityFishAttributes(entity, size, species, uid, name);
            }

            long time = Environment.TickCount64;

            lock (PendingCapturesByPlayer)
            {
                var data = new FishData
                {
                    Size = size,
                    Species = species,
                    PlayerUid = uid,
                    PlayerName = name,
                    Pos = entity.Pos?.XYZ,
                    Timestamp = time
                };

                if (!string.IsNullOrEmpty(playerUid))
                {
                    PendingCapturesByPlayer[playerUid] = data;
                }
                PendingCapturesByPlayer["_last_"] = data;
            }
        }

        public static bool TryApplyCapture(string playerUid, ItemStack stack)
        {
            if (stack == null) return false;
            long time = Environment.TickCount64;

            lock (PendingCapturesByPlayer)
            {
                FishData entry = null;
                if (!string.IsNullOrEmpty(playerUid) && PendingCapturesByPlayer.TryGetValue(playerUid, out var pEntry))
                {
                    if (time - pEntry.Timestamp <= 20000)
                    {
                        entry = pEntry;
                    }
                }

                if (entry == null && PendingCapturesByPlayer.TryGetValue("_last_", out var lastEntry))
                {
                    if (time - lastEntry.Timestamp <= 20000)
                    {
                        entry = lastEntry;
                    }
                }

                if (entry != null && entry.Size > 0f)
                {
                    stack.Attributes.SetFloat("fishSize", entry.Size);
                    stack.Attributes.SetString("speciesCode", entry.Species);
                    if (!string.IsNullOrEmpty(entry.PlayerUid)) stack.Attributes.SetString("caughtByPlayerUid", entry.PlayerUid);
                    if (!string.IsNullOrEmpty(entry.PlayerName)) stack.Attributes.SetString("caughtByPlayerName", entry.PlayerName);
                    return true;
                }
            }
            return false;
        }

        private static void CleanExpired(List<FishData> list, long time)
        {
            list.RemoveAll(d => time - d.Timestamp > 25000); // 25s TTL
        }
    }

    public class FishEntityBehavior : EntityBehavior
    {
        public string SpeciesCode { get; set; } = "trout-rainbow";

        public FishEntityBehavior(Entity entity) : base(entity)
        {
        }

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);
            if (attributes != null && attributes.KeyExists("speciesCode"))
            {
                SpeciesCode = attributes["speciesCode"].AsString("trout-rainbow");
            }
            else
            {
                string code = entity.Code?.Path;
                SpeciesCode = ExtractSpeciesFromCode(code);
            }

            // Check if size already present on entity (watched or regular)
            float existingSize = FishTransferManager.GetEntityFishSize(entity);
            if (existingSize > 0f)
            {
                // Ensure mirrored to both attribute stores and scaled visually
                string uid = entity.WatchedAttributes?.GetString("caughtByPlayerUid") ?? entity.Attributes?.GetString("caughtByPlayerUid", "");
                string name = entity.WatchedAttributes?.GetString("caughtByPlayerName") ?? entity.Attributes?.GetString("caughtByPlayerName", "");
                FishTransferManager.SetEntityFishAttributes(entity, existingSize, SpeciesCode, uid, name);
            }
            else
            {
                if (FishTransferManager.TryApplyPlacement(entity))
                {
                    return;
                }

                // Naturally spawned live fish in the world have NO size until caught!
            }

            if (entity.WatchedAttributes != null)
            {
                entity.WatchedAttributes.RegisterModifiedListener("fishSize", () =>
                {
                    float s = FishTransferManager.GetEntityFishSize(entity);
                    if (s > 0f && entity.Properties?.Client != null)
                    {
                        bool isJuv = entity.Code?.Path?.EndsWith("juvenile") == true;
                        var r = FishSpeciesConfig.GetRange(SpeciesCode, isJuv);
                        float ratio = Math.Clamp((s - r.MinSizeCm) / Math.Max(1f, (r.MaxSizeCm - r.MinSizeCm)), 0f, 1f);
                        entity.Properties.Client.Size = 0.65f + (ratio * 0.90f);
                    }
                });
            }
        }

        public override void OnEntityDeath(DamageSource damageSourceForDeath)
        {
            base.OnEntityDeath(damageSourceForDeath);

            float sizeCm = FishTransferManager.GetEntityFishSize(entity);
            string uid = entity.WatchedAttributes?.GetString("caughtByPlayerUid") ?? entity.Attributes?.GetString("caughtByPlayerUid", "");
            string name = entity.WatchedAttributes?.GetString("caughtByPlayerName") ?? entity.Attributes?.GetString("caughtByPlayerName", "");

            // Stamp caughtByPlayerUid attribute if killed by player
            if (damageSourceForDeath?.SourceEntity is EntityPlayer entityPlayer && entityPlayer.Player is IServerPlayer player)
            {
                uid = player.PlayerUID;
                name = player.PlayerName;
            }

            if (sizeCm <= 0f)
            {
                bool isJuvenile = entity.Code?.Path?.EndsWith("juvenile") == true;
                sizeCm = FishItemBehavior.RollRandomSize(SpeciesCode, isJuvenile);
            }

            FishTransferManager.SetEntityFishAttributes(entity, sizeCm, SpeciesCode, uid, name);
            FishTransferManager.RecordDeath(entity);
        }

        public static bool IsFishEntity(Entity entity)
        {
            if (entity == null || entity.Code == null) return false;
            if (entity.Properties?.Class == "EntityFish") return true;

            string path = entity.Code.Path.ToLowerInvariant();
            if (path.Contains("silverfish") || path.Contains("armor") || path.Contains("weapon")) return false;

            if (path.StartsWith("fish-") || path.Contains("/fish/") || path.Contains("fish") || FishItemBehavior.MatchesAnySpecies(path))
            {
                return true;
            }

            return false;
        }

        public static string ExtractSpeciesFromCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return "trout-rainbow";

            string clean = code.ToLowerInvariant();
            if (clean.Contains(":")) clean = clean.Split(':')[1];

            // 1. Direct dictionary match on the whole code
            if (FishSpeciesConfig.Species.ContainsKey(clean)) return clean;

            // 2. Check each registered species key in species.json to see if code contains it
            foreach (var kvp in FishSpeciesConfig.Species.OrderByDescending(k => k.Key.Length))
            {
                if (clean.Contains(kvp.Key))
                {
                    return kvp.Key;
                }
            }

            // 3. Fallback scan by subparts
            string[] parts = clean.Split('-');
            for (int i = 0; i < parts.Length; i++)
            {
                if (i + 1 < parts.Length)
                {
                    string pair = $"{parts[i]}-{parts[i + 1]}";
                    if (FishSpeciesConfig.Species.ContainsKey(pair)) return pair;
                }
                if (FishSpeciesConfig.Species.ContainsKey(parts[i]))
                {
                    return parts[i];
                }
            }

            return "trout-rainbow";
        }

        public override string PropertyName()
        {
            return "FishEntityBehavior";
        }
    }

    public class FishItemBehavior : CollectibleBehavior, IContainedMeshSource
    {
        public FishItemBehavior(CollectibleObject coll) : base(coll)
        {
        }

        public MeshData GenMesh(ItemSlot slot, ITextureAtlasAPI targetAtlas, BlockPos atBlockPos)
        {
            if (slot?.Itemstack == null || collObj == null) return null;
            ICoreClientAPI capi = AnglersCatchModSystem.ClientApi;
            if (capi == null) return null;

            MeshData mesh = null;
            if (collObj is Item item)
            {
                ITexPositionSource texSource = capi.Tesselator.GetTextureSource(item);
                if (targetAtlas != null && targetAtlas != capi.ItemTextureAtlas)
                {
                    var locs = new Dictionary<string, AssetLocation>();
                    if (item.Textures != null)
                    {
                        foreach (var kvp in item.Textures) locs[kvp.Key] = kvp.Value.Base;
                    }
                    texSource = new ContainedTextureSource(capi, targetAtlas, locs, "fish");
                }
                capi.Tesselator.TesselateItem(item, out mesh, texSource);
            }
            else if (collObj is Block block)
            {
                capi.Tesselator.TesselateBlock(block, out mesh);
            }

            if (mesh != null)
            {
                float sizeCm = slot.Itemstack.Attributes.GetFloat("fishSize", 0f);
                if (sizeCm > 0f)
                {
                    string species = GetSpeciesCode(slot.Itemstack);
                    bool isJuvenile = slot.Itemstack.Collectible.Code.Path.EndsWith("juvenile");
                    var range = FishSpeciesConfig.GetRange(species, isJuvenile);
                    float ratio = Math.Clamp((sizeCm - range.MinSizeCm) / Math.Max(1f, (range.MaxSizeCm - range.MinSizeCm)), 0f, 1f);
                    float scaleFactor = 0.65f + (ratio * 0.90f);

                    mesh = mesh.Clone();
                    Vec3f pivot = new Vec3f(0.5f, 0.05f, 0.5f);
                    if (slot.Itemstack.Collectible.Attributes?["scalePivot"].Exists == true)
                    {
                        pivot = slot.Itemstack.Collectible.Attributes["scalePivot"].AsObject<Vec3f>(pivot);
                    }
                    mesh.Scale(pivot, scaleFactor, scaleFactor, scaleFactor);
                }
            }

            return mesh;
        }

        public string GetMeshCacheKey(ItemSlot slot)
        {
            if (slot?.Itemstack == null) return collObj.Code.ToShortString();
            float sizeCm = slot.Itemstack.Attributes.GetFloat("fishSize", 0f);
            return collObj.Code.ToShortString() + "-sz" + sizeCm.ToString("F1");
        }

        public static bool MatchesAnySpecies(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            foreach (var key in FishSpeciesConfig.Species.Keys)
            {
                if (path.Contains(key)) return true;
            }
            return false;
        }

        public static bool IsFishCollectible(CollectibleObject coll)
        {
            if (coll == null || coll.Code == null) return false;

            string path = coll.Code.Path.ToLowerInvariant();

            // Strict exclusion for non-fish items, tools, and processed fish products/fillets
            if (path.StartsWith("fish-") || 
                path.StartsWith("fishchunk") ||
                path.Contains("fillet") ||
                path.Contains("cooked") ||
                path.Contains("smoked") ||
                path.Contains("cured") ||
                path.Contains("salted") ||
                path.Contains("rod") || 
                path.Contains("hook") || 
                path.Contains("net") || 
                path.Contains("lure") || 
                path.Contains("line") || 
                path.Contains("bait") ||
                path.Contains("spear") ||
                path.Contains("carpet") ||
                path.Contains("recipe") ||
                path.Contains("silverfish") ||
                path.Contains("weapon") ||
                path.Contains("armor"))
            {
                return false;
            }

            // Explicit attribute flag in JSON (if set on whole fish)
            if (coll.Attributes?["isFish"]?.AsBool() == true) return true;

            // Raw whole fish, live fish, or mounted fish taxidermy
            if (path.StartsWith("fishraw-") || 
                path.StartsWith("creature-fish-") ||
                path.StartsWith("fishlive-") ||
                (path.StartsWith("taxidermy-") && (path.Contains("fish") || MatchesAnySpecies(path))))
            {
                return true;
            }

            // Direct match against known fish species database for whole fish
            if (MatchesAnySpecies(path) && (path.Contains("fishraw") || path.Contains("creature") || path.Contains("fishlive") || path.Contains("taxidermy") || path.Contains("freshwater") || path.Contains("saltwater") || path.Contains("reef")))
            {
                return true;
            }

            return false;
        }

        public override void GetHeldItemName(StringBuilder sb, ItemStack itemStack)
        {
            base.GetHeldItemName(sb, itemStack);

            if (itemStack?.Collectible?.Code?.Path?.StartsWith("taxidermy") == true && itemStack.Attributes != null && itemStack.Attributes.HasAttribute("fishSize"))
            {
                float sizeCm = itemStack.Attributes.GetFloat("fishSize", 0f);
                if (sizeCm > 0f)
                {
                    sb.Append(Lang.Get("anglerscatch:taxidermy-length", sizeCm.ToString("F1")));
                }
            }
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

            if (inSlot?.Itemstack != null && IsFishCollectible(inSlot.Itemstack.Collectible))
            {
                IPlayer player = (world as IClientWorldAccessor)?.Player;
                float sizeCm = EnsureFishAttributes(inSlot.Itemstack, world, player);
                if (sizeCm <= 0f) return;

                string species = GetSpeciesCode(inSlot.Itemstack);
                bool isJuvenile = inSlot.Itemstack.Collectible.Code.Path.EndsWith("juvenile");
                var range = FishSpeciesConfig.GetRange(species, isJuvenile);

                if (sizeCm >= range.TrophyThreshold)
                {
                    dsc.AppendLine(Lang.Get("anglerscatch:tooltip-size-trophy", sizeCm.ToString("F1")));
                }
                else
                {
                    dsc.AppendLine(Lang.Get("anglerscatch:tooltip-size-normal", sizeCm.ToString("F1")));
                }

                float weightKg = range.GetWeightKg(sizeCm);
                dsc.AppendLine(Lang.Get("anglerscatch:tooltip-weight", weightKg.ToString("F2")));

                string catcherName = inSlot.Itemstack.Attributes.GetString("caughtByPlayerName");
                if (!string.IsNullOrEmpty(catcherName))
                {
                    dsc.AppendLine(Lang.Get("anglerscatch:tooltip-caught-by", catcherName));
                }
            }
        }

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handHandling, ref EnumHandling handling)
        {
            base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handHandling, ref handling);

            if (slot?.Itemstack != null && IsFishCollectible(slot.Itemstack.Collectible) && blockSel?.Position != null)
            {
                string playerUid = (byEntity as EntityPlayer)?.PlayerUID;
                FishTransferManager.RecordPlacement(slot.Itemstack, blockSel.Position.ToVec3d(), playerUid);
            }
        }

        public override void OnHeldInteractStop(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandling handling)
        {
            base.OnHeldInteractStop(secondsUsed, slot, byEntity, blockSel, entitySel, ref handling);

            if (slot?.Itemstack != null && IsFishCollectible(slot.Itemstack.Collectible))
            {
                if (blockSel?.Position != null)
                {
                    string playerUid = (byEntity as EntityPlayer)?.PlayerUID;
                    FishTransferManager.RecordPlacement(slot.Itemstack, blockSel.Position.ToVec3d(), playerUid);
                }
            }
        }

        public static void OnItemSlotModified(IWorldAccessor world, ItemSlot slot, ItemStack extractedStack = null)
        {
            if (slot?.Itemstack != null && IsFishCollectible(slot.Itemstack.Collectible))
            {
                IPlayer player = (slot.Inventory as InventoryBasePlayer)?.Player;

                // Check if this item in slot came from capturing a swimming fish
                if (player != null && !slot.Itemstack.Attributes.HasAttribute("fishSize"))
                {
                    FishTransferManager.TryApplyCapture(player.PlayerUID, slot.Itemstack);
                }

                EnsureFishAttributes(slot.Itemstack, world, player);

                // Handle taxidermy barrel processing and attribute inheritance
                if (slot.Inventory != null && slot.Inventory.ClassName == "barrel" && world?.Side == EnumAppSide.Server)
                {
                    string id = slot.Inventory.InventoryID;
                    if (world.Api is ICoreServerAPI sapi)
                    {
                        if (slot.Itemstack.Collectible.Code.Path.StartsWith("taxidermy"))
                        {
                            // Taxidermy fish created inside barrel -> inherit stored fish attributes
                            byte[] data = sapi.WorldManager.SaveGame.GetData("barrel_taxidermy_" + id);
                            if (data != null)
                            {
                                string payload = Encoding.UTF8.GetString(data);
                                string[] parts = payload.Split('|');
                                if (parts.Length >= 3)
                                {
                                    if (float.TryParse(parts[0], out float size)) slot.Itemstack.Attributes.SetFloat("fishSize", size);
                                    slot.Itemstack.Attributes.SetString("speciesCode", parts[1]);
                                    slot.Itemstack.Attributes.SetString("caughtByPlayerUid", parts[2]);
                                    if (parts.Length >= 4)
                                    {
                                        slot.Itemstack.Attributes.SetString("caughtByPlayerName", parts[3]);
                                    }
                                }
                            }
                        }
                        else
                        {
                            // Raw/live fish placed in barrel -> save metadata in case it gets cured
                            float size = slot.Itemstack.Attributes.GetFloat("fishSize", 0f);
                            string spec = slot.Itemstack.Attributes.GetString("speciesCode", "");
                            string uid = slot.Itemstack.Attributes.GetString("caughtByPlayerUid", "");
                            string cName = slot.Itemstack.Attributes.GetString("caughtByPlayerName", "");

                            string payload = $"{size}|{spec}|{uid}|{cName}";
                            sapi.WorldManager.SaveGame.StoreData("barrel_taxidermy_" + id, Encoding.UTF8.GetBytes(payload));
                        }
                    }
                }
            }
        }

        public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
        {
            base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
            
            if (target == EnumItemRenderTarget.Gui) return;
            if (!IsFishCollectible(itemstack?.Collectible)) return;
            
            float sizeCm = EnsureFishSize(itemstack, capi?.World);
            if (sizeCm <= 0f) return;

            string species = GetSpeciesCode(itemstack);
            bool isJuvenile = itemstack.Collectible.Code.Path.EndsWith("juvenile");
            var range = FishSpeciesConfig.GetRange(species, isJuvenile);
            
            float ratio = Math.Clamp((sizeCm - range.MinSizeCm) / Math.Max(1f, (range.MaxSizeCm - range.MinSizeCm)), 0f, 1f);
            float scaleFactor = 0.65f + (ratio * 0.90f);

            ModelTransform newTransform = renderinfo.Transform.Clone();
            
            Vec3f pivot = new Vec3f(0.5f, 0.05f, 0.5f);
            if (itemstack.Collectible.Attributes?["scalePivot"].Exists == true)
            {
                pivot = itemstack.Collectible.Attributes["scalePivot"].AsObject<Vec3f>(pivot);
            }

            // Calculate V = Pivot - Origin
            float vx = pivot.X - newTransform.Origin.X;
            float vy = pivot.Y - newTransform.Origin.Y;
            float vz = pivot.Z - newTransform.Origin.Z;

            // Apply existing transform scale
            vx *= newTransform.ScaleXYZ.X;
            vy *= newTransform.ScaleXYZ.Y;
            vz *= newTransform.ScaleXYZ.Z;

            // Apply existing rotation
            float[] mat = Mat4f.Create();
            Mat4f.RotateX(mat, mat, newTransform.Rotation.X * GameMath.DEG2RAD);
            Mat4f.RotateY(mat, mat, newTransform.Rotation.Y * GameMath.DEG2RAD);
            Mat4f.RotateZ(mat, mat, newTransform.Rotation.Z * GameMath.DEG2RAD);

            float[] vec4 = new float[] { vx, vy, vz, 1f };
            float[] res4 = new float[4];
            Mat4f.MulWithVec4(mat, vec4, res4);

            // Calculate required offset to keep pivot anchored
            float offsetX = res4[0] * (1f - scaleFactor);
            float offsetY = res4[1] * (1f - scaleFactor);
            float offsetZ = res4[2] * (1f - scaleFactor);

            newTransform.Translation.X += offsetX;
            newTransform.Translation.Y += offsetY;
            newTransform.Translation.Z += offsetZ;

            newTransform.ScaleXYZ.X *= scaleFactor;
            newTransform.ScaleXYZ.Y *= scaleFactor;
            newTransform.ScaleXYZ.Z *= scaleFactor;
            renderinfo.Transform = newTransform;
        }

        public static float GetFilletMultiplier(float sizeCm, string species, bool isJuvenile)
        {
            var range = FishSpeciesConfig.GetRange(species, isJuvenile);
            float sizeRatio = Math.Clamp((sizeCm - range.MinSizeCm) / Math.Max(1f, (range.MaxSizeCm - range.MinSizeCm)), 0f, 1f);
            float multiplier = 0.8f + (sizeRatio * 0.4f);
            if (sizeCm >= range.TrophyThreshold)
            {
                multiplier += 0.3f;
            }
            return Math.Clamp(multiplier, 0.5f, 2.0f);
        }

        public static string GetSpeciesCode(ItemStack stack)
        {
            if (stack == null) return "trout-rainbow";
            
            string savedSpecies = stack.Attributes?.GetString("speciesCode");
            if (!string.IsNullOrEmpty(savedSpecies)) return savedSpecies;
            
            return FishEntityBehavior.ExtractSpeciesFromCode(stack.Collectible?.Code?.Path);
        }

        public static float RollRandomSize(string species, bool isJuvenile)
        {
            var range = FishSpeciesConfig.GetRange(species, isJuvenile);
            double roll = Random.Shared.NextDouble();
            return (float)Math.Round(range.MinSizeCm + roll * (range.MaxSizeCm - range.MinSizeCm), 1);
        }

        public static float EnsureFishAttributes(ItemStack stack, IWorldAccessor world = null, IPlayer player = null)
        {
            if (stack?.Collectible == null) return 0f;
            if (!IsFishCollectible(stack.Collectible)) return 0f;

            float sizeCm = stack.Attributes.GetFloat("fishSize", 0f);
            string species = stack.Attributes.GetString("speciesCode");

            if (string.IsNullOrEmpty(species))
            {
                species = GetSpeciesCode(stack);
                stack.Attributes.SetString("speciesCode", species);
            }

            if (sizeCm <= 0f)
            {
                // First attempt to retrieve from pending capture transfer (e.g. from pickup/catch)
                if (FishTransferManager.TryApplyCapture(player?.PlayerUID, stack))
                {
                    return stack.Attributes.GetFloat("fishSize", 0f);
                }

                // Strictly Server-Authoritative: Never generate a random size on the client side
                if (world != null && world.Side == EnumAppSide.Client)
                {
                    return 0f;
                }

                bool isJuvenile = stack.Collectible.Code.Path.EndsWith("juvenile");
                sizeCm = RollRandomSize(species, isJuvenile);
                stack.Attributes.SetFloat("fishSize", sizeCm);
            }

            if (player != null && !stack.Attributes.HasAttribute("caughtByPlayerName"))
            {
                stack.Attributes.SetString("caughtByPlayerUid", player.PlayerUID);
                stack.Attributes.SetString("caughtByPlayerName", player.PlayerName);
            }

            return sizeCm;
        }

        public static float GenerateFishSizeOnCatch(ItemStack stack, IWorldAccessor world)
        {
            return EnsureFishAttributes(stack, world);
        }

        public static float EnsureFishSize(ItemStack stack, IWorldAccessor world = null)
        {
            return EnsureFishAttributes(stack, world);
        }
    }
}
