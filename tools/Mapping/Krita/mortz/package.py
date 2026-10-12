"""Writes a map package: background.png, solid.png, destructible.png and map.toml. No Krita
imports."""

import json
import os
import shutil
import uuid


def to_toml(manifest):
    lines = [f"name = {_string(manifest['name'])}",
             f"suggested_players = {manifest['suggested_players']}"]
    for spawn in manifest["spawn_points"]:
        lines += ["[[spawn_points]]", f"x = {spawn['x']}", f"y = {spawn['y']}"]
        if "team" in spawn:
            lines.append(f"team = {_string(spawn['team'])}")
    for zone in manifest["zones"]:
        lines += ["[[zones]]",
                  f"name = {_string(zone['name'])}",
                  f"tags = [{', '.join(_string(tag) for tag in zone['tags'])}]",
                  "[zones.shape]"]
        lines += [f"{key} = {_value(value)}" for key, value in zone["shape"].items()]
        for effect in zone["effects"]:
            lines += ["[[zones.effects]]",
                      f"stat = {_string(effect['stat'])}",
                      f"op = {_string(effect['op'])}",
                      f"value = {_value(effect['value'])}"]
    return "\n".join(lines) + "\n"


def write(maps_directory, map_id, files):
    """Replaces maps_directory/map_id with exactly these files ({name: bytes}). The old folder is
    only removed once the new one is complete, so a failure leaves it untouched."""
    # The game reads the PNGs itself. This keeps Godot from importing them.
    os.makedirs(maps_directory, exist_ok=True)
    open(os.path.join(maps_directory, ".gdignore"), "a").close()

    target = os.path.join(maps_directory, map_id)
    transaction = uuid.uuid4().hex
    staging = os.path.join(maps_directory, f".{map_id}.staging-{transaction}")
    backup = os.path.join(maps_directory, f".{map_id}.backup-{transaction}")
    os.makedirs(staging)
    try:
        for name, data in files.items():
            with open(os.path.join(staging, name), "wb") as file:
                file.write(data)
        if os.path.exists(target):
            os.replace(target, backup)
        os.replace(staging, target)
    except BaseException:
        if os.path.exists(backup) and not os.path.exists(target):
            os.replace(backup, target)
        shutil.rmtree(staging, ignore_errors=True)
        raise
    shutil.rmtree(backup, ignore_errors=True)
    return target


def _string(value):
    # A JSON string is a valid TOML basic string.
    return json.dumps(value, ensure_ascii=False)


def _value(value):
    if isinstance(value, str):
        return _string(value)
    return repr(value)
