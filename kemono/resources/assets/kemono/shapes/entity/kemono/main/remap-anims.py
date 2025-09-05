import json

# Define specific name mappings
name_mapping = {
    "b_FootUpperR": "UpperFootR",
    "b_FootUpperL": "UpperFootL",
    "b_FootLowerR": "LowerFootR",
    "b_FootLowerL": "LowerFootL"
}

# Reverse mapping for lookup from old to new
reverse_mapping = {v: k for k, v in name_mapping.items()}

# Load JSON files
with open('kemono0.json', 'r') as f1:
    data1 = json.load(f1)

with open('kemono1.json', 'r') as f2:
    data2 = json.load(f2)

# Build animation lookup from file2 by "code"
anim_lookup2 = {anim["code"]: anim for anim in data2["animations"]}

# Process each animation in file1
for anim1 in data1["animations"]:
    code = anim1["code"]
    anim2 = anim_lookup2.get(code)

    if not anim2:
        continue  # No matching animation in file2

    # Assume keyframes are aligned by index
    for kf1, kf2 in zip(anim1["keyframes"], anim2["keyframes"]):
        elements1 = kf1.get("elements", {})
        elements2 = kf2.get("elements", {})

        for key, value in elements2.items():
            elements1[key] = value

        # Assign updated elements back
        kf1["elements"] = elements1

# Write the merged result
with open('merged_output.json', 'w') as fout:
    json.dump(data1, fout, indent=2)
