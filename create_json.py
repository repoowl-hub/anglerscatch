import re
import json
import os

with open('src/FishSpeciesConfig.cs', 'r') as f:
    text = f.read()

pattern = re.compile(r'new FishSpeciesRange\(\s*"([^"]+)"\s*,\s*"([^"]+)"\s*,\s*([\d\.]+)f\s*,\s*([\d\.]+)f\s*,\s*([\d\.]+)f\s*,\s*([\d\.]+)f\s*\)')
matches = pattern.findall(text)

species = {}
for match in matches:
    code, name, minS, maxS, minW, maxW = match
    species[code] = {
        'SpeciesCode': code,
        'DisplayName': name,
        'MinSizeCm': float(minS),
        'MaxSizeCm': float(maxS),
        'MinWeightKg': float(minW),
        'MaxWeightKg': float(maxW)
    }

os.makedirs('assets/anglerscatch/config', exist_ok=True)
with open('assets/anglerscatch/config/species.json', 'w') as f:
    json.dump(species, f, indent=4)

print(f'Successfully wrote {len(species)} species to species.json.')
