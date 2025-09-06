import json

# Load JSON files
with open('kemono0.json', 'r') as f1:
    data = json.load(f1)

# Process each animation in file1
for anim in data["animations"]:
    code = anim["code"]

    for keyframe in anim["keyframes"]:
        elements = keyframe.get("elements", {})

        for key, value in elements.items():
            if key == "b_FootUpperR" or key == "b_FootUpperL":
                if "rotationY" in elements[key].keys():
                    elements[key]["rotationY"] = -elements[key]["rotationY"]

        # Assign updated elements back
        keyframe["elements"] = elements

# Write the merged result
with open('inverted_FootUpper_rotationY.json', 'w') as fout:
    json.dump(data, fout, indent=2)
