import json

upper_body = {
    "LowerTorso",
    "UpperTorso",
    "LowerArmL",
    "LowerArmR",
    "UpperArmL",
    "UpperArmR",
    "ItemAnchor",
    "ItemAnchorL"
}

# Load JSON files
with open('legswork.json', 'r') as f1:
    data1 = json.load(f1)

with open('upperbodyworks.json', 'r') as f2:
    data2 = json.load(f2)

# Build animation lookup from leg animations by "code"
body_animations = {anim["code"]: anim for anim in data2["animations"]}

for leg_anim in data1["animations"]:
    code = leg_anim["code"]
    body_anim = body_animations.get(code)

    if not body_anim:
        continue

    for leg_keyframe, body_keyframe in zip(leg_anim["keyframes"], body_anim["keyframes"]):
        for key in leg_keyframe["elements"].keys():
            if key in upper_body:
                
                try:
                    leg_keyframe["elements"][key] = body_keyframe["elements"][key]
                    print("Updating " + code + "/keyframes/" + str(leg_anim["keyframes"].index(leg_keyframe)) + "/" + key)
                except KeyError:
                    leg_keyframe["elements"][key] = {}
                    print("Deleting " + code + "/keyframes/" + str(leg_anim["keyframes"].index(leg_keyframe)) + "/" + key)
                except Exception as e:
                    print("ERROR PROCESSING " + code + "/keyframes/" + str(leg_anim["keyframes"].index(leg_keyframe)) + "/" + key)
                    raise e

# Write the merged result
with open('merged_output.json', 'w') as fout:
    json.dump(data1, fout, indent=2)
