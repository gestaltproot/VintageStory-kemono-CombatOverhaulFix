import json

bad_bones = [
    "b_TorsoUpper",
    "b_TorsoLower",
    "b_ArmUpperL",
    "b_ArmUpperR",
    "b_ArmLowerL",
    "b_ArmLowerR",
    "b_ItemAnchor",
    "b_ItemAncorL"
]


# Load JSON files
with open('kemono0.json', 'r') as f1:
    data = json.load(f1)

# Process each animation in file1
for anim in data["animations"]:
    code = anim["code"]

    for keyframe in anim["keyframes"]:
        elements = keyframe.get("elements", {})
        newElems = {}

        for key, value in elements.items():
            if key not in bad_bones:
                newElems[key] = value

        # Assign updated elements back
        keyframe["elements"] = newElems

# Write the merged result
with open('cleaned-kemono0.json', 'w') as fout:
    json.dump(data, fout, indent=2)
    