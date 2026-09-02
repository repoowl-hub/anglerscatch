using System;
using System.Collections.Generic;

namespace AnglersCatch
{
    public class FishSpeciesRange
    {
        public string SpeciesCode { get; set; }
        public string DisplayName { get; set; }
        public float MinSizeCm { get; set; }
        public float MaxSizeCm { get; set; }
        public float MinWeightKg { get; set; }
        public float MaxWeightKg { get; set; }
        public bool IsJuvenile { get; set; } = false;

        public FishSpeciesRange(string code, string displayName, float minSizeCm, float maxSizeCm, float minWeightKg, float maxWeightKg, bool isJuvenile = false)
        {
            SpeciesCode = code;
            DisplayName = displayName;
            MinSizeCm = minSizeCm;
            MaxSizeCm = maxSizeCm;
            MinWeightKg = minWeightKg;
            MaxWeightKg = maxWeightKg;
            IsJuvenile = isJuvenile;
        }

        // Juvenile fish can NEVER be trophy catches!
        public float TrophyThreshold => IsJuvenile ? float.MaxValue : MinSizeCm + 0.80f * (MaxSizeCm - MinSizeCm);

        /// <summary>
        /// Dynamically calculates weight in kg based on length in cm using a cubic growth curve between MinWeightKg and MaxWeightKg.
        /// </summary>
        public float GetWeightKg(float sizeCm)
        {
            float ratio = Math.Clamp((sizeCm - MinSizeCm) / Math.Max(1f, MaxSizeCm - MinSizeCm), 0f, 1f);
            // Cubic scaling curve (weight grows non-linearly with length)
            float weightRatio = (float)Math.Pow(ratio, 2.5);
            float weight = MinWeightKg + weightRatio * (MaxWeightKg - MinWeightKg);
            return (float)Math.Round(weight, 2);
        }
    }

    public static class FishSpeciesConfig
    {
        public static Dictionary<string, FishSpeciesRange> Species = new Dictionary<string, FishSpeciesRange>();

        public static void LoadFrom(Vintagestory.API.Common.ICoreAPI api)
        {
            try
            {
                var loc = new Vintagestory.API.Common.AssetLocation("anglerscatch", "config/species.json");
                var asset = api.Assets.Get(loc);
                if (asset != null)
                {
                    var dict = asset.ToObject<Dictionary<string, FishSpeciesRange>>();
                    if (dict != null)
                    {
                        Species = dict;
                        api.Logger.Notification($"[Anglers Catch] Successfully loaded {Species.Count} fish species from config.");
                    }
                }
                else
                {
                    api.Logger.Error("[Anglers Catch] Failed to find config/species.json asset! Falling back to empty species list.");
                }
            }
            catch (Exception ex)
            {
                api.Logger.Error($"[Anglers Catch] Exception loading species.json: {ex.Message}");
            }
        }

        public static void RegisterSpecies(FishSpeciesRange range)
        {
            if (range == null || string.IsNullOrWhiteSpace(range.SpeciesCode)) return;
            Species[range.SpeciesCode.ToLowerInvariant()] = range;
        }

        public static FishSpeciesRange GetRange(string speciesCode, bool isJuvenile = false)
        {
            FishSpeciesRange range = null;
            if (!string.IsNullOrWhiteSpace(speciesCode))
            {
                string key = speciesCode.ToLowerInvariant();
                if (!Species.TryGetValue(key, out range))
                {
                    string[] parts = key.Split('-');
                    if (parts.Length >= 2)
                    {
                        Species.TryGetValue(parts[0], out range);
                    }
                }
            }

            if (range == null)
            {
                range = new FishSpeciesRange(speciesCode ?? "unknown", speciesCode ?? "Unknown Fish", 20.0f, 80.0f, 0.30f, 8.00f);
            }

            if (isJuvenile)
            {
                // Juvenile fish size range is 30% - 45% of full adult species length and weight (and never trophy catches)
                return new FishSpeciesRange(
                    range.SpeciesCode + "-juvenile",
                    "Juvenile " + range.DisplayName,
                    (float)Math.Round(range.MinSizeCm * 0.30f, 1),
                    (float)Math.Round(range.MaxSizeCm * 0.45f, 1),
                    (float)Math.Round(range.MinWeightKg * 0.10f, 2),
                    (float)Math.Round(range.MaxWeightKg * 0.20f, 2),
                    isJuvenile: true
                );
            }

            return range;
        }
    }
}
