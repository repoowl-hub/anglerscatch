using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace AnglersCatch
{
    public class ItemFishRaw : Item, IContainedMeshSource
    {
        public MeshData GenMesh(ItemSlot slot, ITextureAtlasAPI targetAtlas, BlockPos atBlockPos)
        {
            if (api is ICoreClientAPI capi && slot?.Itemstack != null)
            {
                ITexPositionSource texSource = capi.Tesselator.GetTextureSource(this);
                if (targetAtlas != null && targetAtlas != capi.ItemTextureAtlas)
                {
                    var locs = new Dictionary<string, AssetLocation>();
                    if (this.Textures != null)
                    {
                        foreach (var kvp in this.Textures) locs[kvp.Key] = kvp.Value.Base;
                    }
                    texSource = new ContainedTextureSource(capi, targetAtlas, locs, "fish");
                }
                
                capi.Tesselator.TesselateItem(this, out MeshData mesh, texSource);
                if (mesh != null)
                {
                    float sizeCm = FishItemBehavior.EnsureFishSize(slot.Itemstack);
                    if (sizeCm > 0f)
                    {
                        string species = FishItemBehavior.GetSpeciesCode(slot.Itemstack);
                        bool isJuvenile = slot.Itemstack.Collectible.Code.Path.EndsWith("juvenile");
                        var range = FishSpeciesConfig.GetRange(species, isJuvenile);
                        float ratio = Math.Clamp((sizeCm - range.MinSizeCm) / Math.Max(1f, (range.MaxSizeCm - range.MinSizeCm)), 0f, 1f);
                        float scaleFactor = 1.0f + (ratio * 0.50f);
                        
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
            return null;
        }

        public string GetMeshCacheKey(ItemSlot slot)
        {
            if (slot?.Itemstack == null) return Code.ToShortString();
            float sizeCm = FishItemBehavior.EnsureFishSize(slot.Itemstack);
            return Code.ToShortString() + "-" + sizeCm.ToString("F1");
        }
    }

    public class ItemFishTaxidermy : Item, IContainedMeshSource
    {
        public MeshData GenMesh(ItemSlot slot, ITextureAtlasAPI targetAtlas, BlockPos atBlockPos)
        {
            if (api is ICoreClientAPI capi && slot?.Itemstack != null)
            {
                ITexPositionSource texSource = capi.Tesselator.GetTextureSource(this);
                if (targetAtlas != null && targetAtlas != capi.ItemTextureAtlas)
                {
                    var locs = new Dictionary<string, AssetLocation>();
                    if (this.Textures != null)
                    {
                        foreach (var kvp in this.Textures) locs[kvp.Key] = kvp.Value.Base;
                    }
                    texSource = new ContainedTextureSource(capi, targetAtlas, locs, "fish");
                }

                capi.Tesselator.TesselateItem(this, out MeshData mesh, texSource);
                if (mesh != null)
                {
                    float sizeCm = FishItemBehavior.EnsureFishSize(slot.Itemstack);
                    if (sizeCm > 0f)
                    {
                        string species = FishItemBehavior.GetSpeciesCode(slot.Itemstack);
                        bool isJuvenile = slot.Itemstack.Collectible.Code.Path.EndsWith("juvenile");
                        var range = FishSpeciesConfig.GetRange(species, isJuvenile);
                        float ratio = Math.Clamp((sizeCm - range.MinSizeCm) / Math.Max(1f, (range.MaxSizeCm - range.MinSizeCm)), 0f, 1f);
                        float scaleFactor = 1.0f + (ratio * 0.50f);
                        
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
            return null;
        }

        public string GetMeshCacheKey(ItemSlot slot)
        {
            if (slot?.Itemstack == null) return Code.ToShortString();
            float sizeCm = FishItemBehavior.EnsureFishSize(slot.Itemstack);
            return Code.ToShortString() + "-" + sizeCm.ToString("F1");
        }
    }
}
