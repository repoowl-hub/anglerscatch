using System;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace AnglersCatch
{
    [HarmonyPatch(typeof(RecipeBase), "GenerateOutputStack")]
    public class GridRecipePatch
    {
        public static void Postfix(ItemSlot[] inputSlots, ItemSlot outputSlot)
        {
            if (outputSlot?.Itemstack == null) return;
            
            // Find if any input was a live/raw whole fish
            ItemStack fishInput = null;
            if (inputSlots != null)
            {
                foreach (var slot in inputSlots)
                {
                    if (slot?.Itemstack != null && FishItemBehavior.IsFishCollectible(slot.Itemstack.Collectible))
                    {
                        fishInput = slot.Itemstack;
                        break;
                    }
                }
            }

            if (fishInput != null)
            {
                // If output is another whole fish (e.g. taxidermy trophy), preserve attributes
                if (FishItemBehavior.IsFishCollectible(outputSlot.Itemstack.Collectible))
                {
                    float size = fishInput.Attributes.GetFloat("fishSize", 0f);
                    if (size > 0f)
                    {
                        outputSlot.Itemstack.Attributes.SetFloat("fishSize", size);
                        
                        string species = fishInput.Attributes.GetString("speciesCode");
                        if (!string.IsNullOrEmpty(species))
                        {
                            outputSlot.Itemstack.Attributes.SetString("speciesCode", species);
                        }

                        string playerName = fishInput.Attributes.GetString("caughtByPlayerName");
                        if (!string.IsNullOrEmpty(playerName))
                        {
                            outputSlot.Itemstack.Attributes.SetString("caughtByPlayerName", playerName);
                        }
                        
                        string playerUid = fishInput.Attributes.GetString("caughtByPlayerUid");
                        if (!string.IsNullOrEmpty(playerUid))
                        {
                            outputSlot.Itemstack.Attributes.SetString("caughtByPlayerUid", playerUid);
                        }
                    }

                    FishItemBehavior.EnsureFishAttributes(outputSlot.Itemstack);
                }
                // If output is meat / fillets (fish-raw, fishchunk, etc.), scale yield by fish size / trophy multiplier
                else if (outputSlot.Itemstack.Collectible.Code.Path.StartsWith("fish-") || outputSlot.Itemstack.Collectible.Code.Path.StartsWith("fishchunk"))
                {
                    float sizeCm = FishItemBehavior.EnsureFishSize(fishInput);
                    string species = FishItemBehavior.GetSpeciesCode(fishInput);
                    bool isJuvenile = fishInput.Collectible.Code.Path.EndsWith("juvenile");
                    float multiplier = FishItemBehavior.GetFilletMultiplier(sizeCm, species, isJuvenile);
                    int newQuantity = (int)Math.Max(1, Math.Round(outputSlot.Itemstack.StackSize * multiplier));
                    outputSlot.Itemstack.StackSize = newQuantity;
                }
            }
            else if (FishItemBehavior.IsFishCollectible(outputSlot.Itemstack.Collectible))
            {
                FishItemBehavior.EnsureFishAttributes(outputSlot.Itemstack);
            }
        }
    }

    [HarmonyPatch(typeof(CollectibleBehaviorGroundStoredProcessable), "OnContainedInteractStop")]
    public class GroundStoredProcessablePatch
    {
        public static void Prefix(float secondsUsed, BlockEntityContainer be, ItemSlot slot, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (slot?.Itemstack != null && FishItemBehavior.IsFishCollectible(slot.Itemstack.Collectible))
            {
                float sizeCm = FishItemBehavior.EnsureFishSize(slot.Itemstack);
                string species = FishItemBehavior.GetSpeciesCode(slot.Itemstack);
                bool isJuvenile = slot.Itemstack.Collectible.Code.Path.EndsWith("juvenile");
                float multiplier = FishItemBehavior.GetFilletMultiplier(sizeCm, species, isJuvenile);

                FishTransferManager.ActiveFlayingMultiplier = multiplier;
                FishTransferManager.ActiveFlayingPlayer = byPlayer?.PlayerUID;
                FishTransferManager.ActiveFlayingPos = blockSel?.Position;
            }
        }

        public static void Postfix()
        {
            FishTransferManager.ActiveFlayingMultiplier = 1f;
            FishTransferManager.ActiveFlayingPlayer = null;
            FishTransferManager.ActiveFlayingPos = null;
        }
    }

    [HarmonyPatch(typeof(BlockDropItemStack), "GetNextItemStack")]
    public class BlockDropItemStackGetNextItemStackPatch
    {
        public static void Prefix(ref float dropQuantityMultiplier)
        {
            if (FishTransferManager.ActiveFlayingMultiplier > 0f && Math.Abs(FishTransferManager.ActiveFlayingMultiplier - 1f) > 0.001f)
            {
                dropQuantityMultiplier *= FishTransferManager.ActiveFlayingMultiplier;
            }
        }
    }

    [HarmonyPatch(typeof(ItemSlot), "Itemstack", MethodType.Setter)]
    public class ItemSlotSetterPatch
    {
        public static void Prefix(ItemSlot __instance, ItemStack value)
        {
            ItemStack oldStack = __instance.Itemstack;
            ItemStack newStack = value;

            if (oldStack != null && newStack != null)
            {
                // If a live fish is being directly replaced by a dead whole fish in the same slot (perish transition)
                if (oldStack.Collectible != null && newStack.Collectible != null && 
                    FishItemBehavior.IsFishCollectible(oldStack.Collectible) && 
                    FishItemBehavior.IsFishCollectible(newStack.Collectible))
                {
                    float size = oldStack.Attributes.GetFloat("fishSize", 0f);
                    if (size > 0f)
                    {
                        newStack.Attributes.SetFloat("fishSize", size);

                        string species = oldStack.Attributes.GetString("speciesCode");
                        if (!string.IsNullOrEmpty(species))
                        {
                            newStack.Attributes.SetString("speciesCode", species);
                        }
                        
                        string pName = oldStack.Attributes.GetString("caughtByPlayerName");
                        if (!string.IsNullOrEmpty(pName))
                        {
                            newStack.Attributes.SetString("caughtByPlayerName", pName);
                        }
                        
                        string pUid = oldStack.Attributes.GetString("caughtByPlayerUid");
                        if (!string.IsNullOrEmpty(pUid))
                        {
                            newStack.Attributes.SetString("caughtByPlayerUid", pUid);
                        }
                    }
                }
            }
        }

        public static void Postfix(ItemSlot __instance, ItemStack value)
        {
            if (value?.Collectible == null) return;

            if (FishItemBehavior.IsFishCollectible(value.Collectible))
            {
                IWorldAccessor world = (__instance.Inventory?.Api as ICoreAPI)?.World;
                IPlayer player = (__instance.Inventory as InventoryBasePlayer)?.Player;

                if (player != null && !value.Attributes.HasAttribute("fishSize"))
                {
                    FishTransferManager.TryApplyCapture(player.PlayerUID, value);
                }

                FishItemBehavior.EnsureFishAttributes(value, world, player);
            }
        }
    }

    [HarmonyPatch(typeof(InventoryBase), "DidModifyItemSlot")]
    public class InventoryBaseDidModifyItemSlotPatch
    {
        public static void Postfix(InventoryBase __instance, ItemSlot slot, ItemStack extractedStack)
        {
            if (slot?.Itemstack != null && FishItemBehavior.IsFishCollectible(slot.Itemstack.Collectible))
            {
                IWorldAccessor world = __instance.Api?.World;
                FishItemBehavior.OnItemSlotModified(world, slot, extractedStack);
            }
        }
    }

    [HarmonyPatch(typeof(EntityItem), "Initialize")]
    public class EntityItemInitializePatch
    {
        public static void Postfix(EntityItem __instance)
        {
            if (__instance?.Slot?.Itemstack == null) return;

            ItemStack stack = __instance.Slot.Itemstack;
            if (stack.Collectible == null) return;

            if (FishItemBehavior.IsFishCollectible(stack.Collectible))
            {
                Vec3d pos = __instance.Pos?.XYZ;
                
                // Inherit exact attributes from a dying fish entity if spawned from death drop
                if (pos != null && FishTransferManager.TryApplyDeathDrop(stack, pos))
                {
                    return;
                }

                FishItemBehavior.EnsureFishAttributes(stack, __instance.World);
            }
        }
    }

    [HarmonyPatch(typeof(ItemCreature), "OnHeldInteractStart")]
    public class ItemCreatureOnHeldInteractStartPatch
    {
        [HarmonyPriority(Priority.First)]
        public static void Prefix(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handHandling)
        {
            if (slot?.Itemstack != null && FishItemBehavior.IsFishCollectible(slot.Itemstack.Collectible) && blockSel?.Position != null)
            {
                string playerUid = (byEntity as EntityPlayer)?.PlayerUID;
                FishTransferManager.RecordPlacement(slot.Itemstack, blockSel.Position.ToVec3d(), playerUid);
            }
        }
    }

    [HarmonyPatch(typeof(EntityFish), "OnInteract")]
    public class EntityFishOnInteractPatch
    {
        [HarmonyPriority(Priority.First)]
        public static void Prefix(EntityFish __instance, EntityAgent byEntity, ItemSlot slot, Vec3d hitPosition, EnumInteractMode mode)
        {
            if (__instance != null)
            {
                string playerUid = (byEntity as EntityPlayer)?.PlayerUID;
                if (string.IsNullOrEmpty(playerUid) && byEntity is EntityPlayer entityPlayer && entityPlayer.Player != null)
                {
                    playerUid = entityPlayer.Player.PlayerUID;
                }

                FishTransferManager.RecordCapture(playerUid, __instance);
            }
        }
    }

    [HarmonyPatch(typeof(EntityPlayer), "TryGiveItemStack")]
    public class EntityPlayerTryGiveItemStackPatch
    {
        public static void Prefix(EntityPlayer __instance, ItemStack itemstack)
        {
            if (itemstack != null && FishItemBehavior.IsFishCollectible(itemstack.Collectible))
            {
                if (!itemstack.Attributes.HasAttribute("fishSize") || itemstack.Attributes.GetFloat("fishSize", 0f) <= 0f)
                {
                    string playerUid = __instance?.PlayerUID;
                    FishTransferManager.TryApplyCapture(playerUid, itemstack);
                }
            }
        }
    }

    [HarmonyPatch(typeof(BlockEntityBarrel), "FindMatchingRecipe", new Type[] { typeof(IPlayer) })]
    public class BlockEntityBarrelFindMatchingRecipePatch
    {
        public static void Postfix(BlockEntityBarrel __instance)
        {
            if (__instance?.CurrentRecipe != null && __instance.Inventory != null && !__instance.Inventory.Empty)
            {
                ItemSlot slot0 = __instance.Inventory[0];
                if (slot0?.Itemstack != null && FishItemBehavior.IsFishCollectible(slot0.Itemstack.Collectible))
                {
                    float size = slot0.Itemstack.Attributes.GetFloat("fishSize", 0f);
                    if (size > 0f)
                    {
                        ItemStack outStack = __instance.CurrentRecipe.RecipeOutput?.ResolvedItemStack;
                        if (outStack != null && outStack.Collectible != null && outStack.Collectible.Code.Path.StartsWith("taxidermy"))
                        {
                            outStack.Attributes.SetFloat("fishSize", size);
                            outStack.Attributes.SetString("speciesCode", slot0.Itemstack.Attributes.GetString("speciesCode", FishItemBehavior.GetSpeciesCode(slot0.Itemstack)));
                            string uid = slot0.Itemstack.Attributes.GetString("caughtByPlayerUid");
                            string name = slot0.Itemstack.Attributes.GetString("caughtByPlayerName");
                            if (!string.IsNullOrEmpty(uid)) outStack.Attributes.SetString("caughtByPlayerUid", uid);
                            if (!string.IsNullOrEmpty(name)) outStack.Attributes.SetString("caughtByPlayerName", name);
                        }
                    }
                }
            }
        }
    }

    public static class DynamicBehaviorPatcher
    {
        public static void PatchExternalModBehaviors(Harmony harmony, ICoreAPI api)
        {
            try
            {
                Assembly ithaniaAsm = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name == "IthaniaExpandedFishing")
                    {
                        ithaniaAsm = asm;
                        break;
                    }
                }

                if (ithaniaAsm != null)
                {
                    // 1. Hook ground stored fillet knife processing for trophy meat scaling
                    Type ithaniaType = ithaniaAsm.GetType("IthaniaExpandedFishing.Common.Behaviors.CollectibleBehaviorFilletKnifeGroundStoredProcessable");
                    if (ithaniaType != null)
                    {
                        var targetMethod = ithaniaType.GetMethod("OnContainedInteractStop", BindingFlags.Public | BindingFlags.Instance);
                        if (targetMethod != null)
                        {
                            var prefix = typeof(GroundStoredProcessablePatch).GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static);
                            var postfix = typeof(GroundStoredProcessablePatch).GetMethod("Postfix", BindingFlags.Public | BindingFlags.Static);
                            harmony.Patch(targetMethod, new HarmonyMethod(prefix), new HarmonyMethod(postfix));
                            api?.Logger?.Notification("[Angler's Catch] Successfully hooked Ithania's CollectibleBehaviorFilletKnifeGroundStoredProcessable for trophy meat scaling.");
                        }
                    }

                    // 2. Hook LiveFishNameCarrier ApplyNameToStack (Entity fish -> ItemStack stack)
                    Type nameCarrierType = ithaniaAsm.GetType("IthaniaExpandedFishing.Common.Behaviors.CollectibleBehaviorLiveFishNameCarrier");
                    if (nameCarrierType != null)
                    {
                        var toStackMethod = nameCarrierType.GetMethod("ApplyNameToStack", BindingFlags.Public | BindingFlags.Static);
                        if (toStackMethod != null)
                        {
                            var postfix = typeof(DynamicBehaviorPatcher).GetMethod("OnApplyNameToStack", BindingFlags.Public | BindingFlags.Static);
                            harmony.Patch(toStackMethod, null, new HarmonyMethod(postfix));
                            api?.Logger?.Notification("[Angler's Catch] Successfully hooked Ithania's ApplyNameToStack for fish attribute preservation.");
                        }

                        var toEntityMethod = nameCarrierType.GetMethod("ApplyNameToEntity", BindingFlags.Public | BindingFlags.Static);
                        if (toEntityMethod != null)
                        {
                            var postfix = typeof(DynamicBehaviorPatcher).GetMethod("OnApplyNameToEntity", BindingFlags.Public | BindingFlags.Static);
                            harmony.Patch(toEntityMethod, null, new HarmonyMethod(postfix));
                            api?.Logger?.Notification("[Angler's Catch] Successfully hooked Ithania's ApplyNameToEntity for fish attribute preservation.");
                        }
                    }

                    // 3. Hook ItemFishNet TryPickupFish (ItemSlot slot, EntityAgent byEntity, Entity fish)
                    Type netType = ithaniaAsm.GetType("IthaniaExpandedFishing.Common.Items.ItemFishNet");
                    if (netType != null)
                    {
                        var tryPickupMethod = netType.GetMethod("TryPickupFish", BindingFlags.Public | BindingFlags.Instance);
                        if (tryPickupMethod != null)
                        {
                            var prefix = typeof(DynamicBehaviorPatcher).GetMethod("OnTryPickupFishPrefix", BindingFlags.Public | BindingFlags.Static);
                            harmony.Patch(tryPickupMethod, new HarmonyMethod(prefix));
                            api?.Logger?.Notification("[Angler's Catch] Successfully hooked Ithania's ItemFishNet.TryPickupFish.");
                        }
                    }
                }

                // 4. Hook Catch & Release mod (crfish) for flopping fish pickups and live rod catches
                Assembly crFishAsm = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name.StartsWith("CatchAndRelease_Fish") || asm.GetName().Name == "crfish")
                    {
                        crFishAsm = asm;
                        break;
                    }
                }

                if (crFishAsm != null)
                {
                    Type crInteractType = crFishAsm.GetType("CatchAndRelease_Fish.code.patches.EntityFish_OnInteract");
                    if (crInteractType != null)
                    {
                        var targetMethod = crInteractType.GetMethod("Prefix", BindingFlags.Public | BindingFlags.Static);
                        if (targetMethod != null)
                        {
                            var prefix = typeof(DynamicBehaviorPatcher).GetMethod("OnCREntityFishInteractPrefix", BindingFlags.Public | BindingFlags.Static);
                            harmony.Patch(targetMethod, new HarmonyMethod(prefix) { priority = Priority.First });
                            api?.Logger?.Notification("[Angler's Catch] Successfully hooked Catch & Release's EntityFish_OnInteract for flopping fish pickups.");
                        }
                    }

                    Type crBobberType = crFishAsm.GetType("CatchAndRelease_Fish.code.patches.EntityBobber_Patches");
                    if (crBobberType != null)
                    {
                        var catchMethod = crBobberType.GetMethod("Patch_CatchWorldFish", BindingFlags.Public | BindingFlags.Static);
                        if (catchMethod != null)
                        {
                            var postfix = typeof(DynamicBehaviorPatcher).GetMethod("OnCRCatchWorldFishPostfix", BindingFlags.Public | BindingFlags.Static);
                            harmony.Patch(catchMethod, null, new HarmonyMethod(postfix));
                            api?.Logger?.Notification("[Angler's Catch] Successfully hooked Catch & Release's rod live fish catch.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                api?.Logger?.Warning($"[Angler's Catch] Could not hook external behaviors: {ex.Message}");
            }
        }

        public static void OnApplyNameToStack(Entity fish, ItemStack stack)
        {
            if (fish != null && stack != null)
            {
                FishTransferManager.TransferEntityToStack(fish, stack);
            }
        }

        public static void OnApplyNameToEntity(ItemStack stack, Entity fish)
        {
            if (stack != null && fish != null)
            {
                FishTransferManager.TransferStackToEntity(stack, fish);
            }
        }

        public static void OnTryPickupFishPrefix(ItemSlot slot, EntityAgent byEntity, Entity fish)
        {
            if (fish != null)
            {
                string playerUid = (byEntity as EntityPlayer)?.PlayerUID;
                FishTransferManager.RecordCapture(playerUid, fish);
            }
        }

        public static void OnCREntityFishInteractPrefix(EntityAgent byEntity, object[] __args)
        {
            try
            {
                Entity fish = null;
                if (__args != null && __args.Length > 4 && __args[4] is Entity e)
                {
                    fish = e;
                }

                if (fish != null)
                {
                    string playerUid = (byEntity as EntityPlayer)?.PlayerUID;
                    if (string.IsNullOrEmpty(playerUid) && byEntity is EntityPlayer entityPlayer && entityPlayer.Player != null)
                    {
                        playerUid = entityPlayer.Player.PlayerUID;
                    }
                    FishTransferManager.RecordCapture(playerUid, fish);
                }
            }
            catch { }
        }

        public static void OnCRCatchWorldFishPostfix(Entity caughtFish, ItemStack[] __result, Entity instance)
        {
            if (caughtFish != null && __result != null && __result.Length > 0 && __result[0] != null)
            {
                FishTransferManager.TransferEntityToStack(caughtFish, __result[0]);
            }
        }
    }
}
