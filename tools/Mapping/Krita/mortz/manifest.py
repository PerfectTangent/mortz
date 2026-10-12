"""Builds and checks a map manifest from what the document holds. No Krita imports."""

import math
import re

from . import geometry

ERROR = "error"
WARNING = "warning"

ZONES = "zones"
SPAWNS = "spawns"

# Antialiased edges are normal. This much partial alpha means a soft brush or a faded layer.
_PARTIAL_ALPHA_WARNING_FRACTION = 0.05


class Result:
    def __init__(self):
        self.issues = []
        self.manifest = None

    @property
    def has_errors(self):
        return any(issue["severity"] == ERROR for issue in self.issues)

    def error(self, message, shape=None):
        self.issues.append({"severity": ERROR, "message": message, "shape": shape})

    def warning(self, message, shape=None):
        self.issues.append({"severity": WARNING, "message": message, "shape": shape})


def build(schema, width, height, x_res, annotation, shapes, solid_alpha, destructible_alpha):
    """shapes: [{"layer", "name", "svg", "transform"}]. *_alpha: one byte per pixel, row-major."""
    result = Result()
    limits = schema["limits"]
    id_pattern = re.compile(schema["idPattern"])

    if not (limits["minWidth"] <= width <= limits["maxSize"] and
            limits["minHeight"] <= height <= limits["maxSize"]):
        result.error(f"the canvas is {width}x{height}, maps must be between "
                     f"{limits['minWidth']}x{limits['minHeight']} and "
                     f"{limits['maxSize']}x{limits['maxSize']}")
        return result

    map_id = annotation.get("id") or ""
    if not id_pattern.fullmatch(map_id):
        result.error(f"map id '{map_id}' is not valid: lowercase letters, digits, '_' and '-', "
                     "starting with a letter or digit")
    name = (annotation.get("name") or "").strip()
    if not name:
        result.error("the map has no name")
    players = annotation.get("suggestedPlayers") or 0
    if not limits["minPlayers"] <= players <= limits["maxPlayers"]:
        result.error(f"suggested players must be between {limits['minPlayers']} and "
                     f"{limits['maxPlayers']}")

    spawn_entries = annotation.get("spawns") or {}
    zone_entries = annotation.get("zones") or {}
    spawns, zones = [], []
    for shape in shapes:
        try:
            shape_geometry = geometry.read(shape["svg"], shape["transform"], x_res)
        except geometry.ShapeError as error:
            result.error(str(error), shape["name"])
            continue
        if shape["layer"] == SPAWNS:
            spawns.append((shape["name"], shape_geometry))
        else:
            zones.append((shape["name"], shape_geometry))

    # The settings stay in the document, so this only costs the author an unused entry.
    shape_names = {shape["name"] for shape in shapes}
    for entry_name in sorted(set(spawn_entries) - shape_names):
        result.warning(f"spawn settings for '{entry_name}' have no shape and are ignored")
    for entry_name in sorted(set(zone_entries) - shape_names):
        result.warning(f"zone settings for '{entry_name}' have no shape and are ignored")

    result.manifest = {
        "name": name,
        "suggested_players": players,
        "spawn_points": [_spawn(result, schema, width, height, shape_name, shape_geometry,
                                spawn_entries.get(shape_name) or {})
                         for shape_name, shape_geometry in spawns],
        "zones": [_zone(result, schema, id_pattern, width, height, shape_name, shape_geometry,
                        zone_entries.get(shape_name) or {})
                  for shape_name, shape_geometry in zones],
    }
    _check_zone_names(result, [shape_name for shape_name, _ in zones])
    effect_zones = sum(1 for zone in result.manifest["zones"] if zone["effects"])
    if effect_zones > limits["maxEffectZones"]:
        result.error(f"the map has {effect_zones} zones with effects, the maximum is "
                     f"{limits['maxEffectZones']}")

    _check_terrain(result, schema, width, height, solid_alpha, destructible_alpha,
                   [shape_name for shape_name, _ in spawns])
    return result


def _spawn(result, schema, width, height, name, shape_geometry, entry):
    x, y = shape_geometry.feet()
    spawn = {"x": x, "y": y}
    team = entry.get("team")
    if team:
        if team in schema["teams"]:
            spawn["team"] = team
        else:
            result.error(f"unknown team '{team}'", name)
    if not (0 <= x < width and 0 <= y < height):
        result.error(f"the spawn's feet at ({x}, {y}) are outside the map", name)
    return spawn


def _zone(result, schema, id_pattern, width, height, name, shape_geometry, entry):
    if not id_pattern.fullmatch(name):
        result.error(f"zone name '{name}' is not valid: lowercase letters, digits, '_' and '-', "
                     "starting with a letter or digit", name)
    effects = []
    for effect in entry.get("effects") or []:
        stat, op, value = effect.get("stat"), effect.get("op"), effect.get("value")
        if stat not in schema["stats"]:
            result.error(f"unknown stat '{stat}'", name)
        elif op not in schema["ops"]:
            result.error(f"unknown op '{op}'", name)
        elif not isinstance(value, (int, float)) or not math.isfinite(value):
            result.error(f"the {stat} effect needs a finite number, not '{value}'", name)
        else:
            effects.append({"stat": stat, "op": op, "value": float(value)})

    shape = shape_geometry.zone_shape()
    left, top, right, bottom = _bounds(shape)
    if left < 0 or top < 0 or right > width or bottom > height:
        result.error("the zone extends outside the map", name)
    return {"name": name, "tags": list(entry.get("tags") or []), "shape": shape,
            "effects": effects}


def _check_zone_names(result, names):
    seen = set()
    for name in names:
        if name in seen:
            result.error(f"two zones are named '{name}'", name)
        seen.add(name)


def _bounds(shape):
    """Axis-aligned bounds, as the game's validator computes them."""
    if shape["type"] == "circle":
        r = shape["radius"]
        return shape["x"] - r, shape["y"] - r, shape["x"] + r, shape["y"] + r
    radians = math.radians(shape["rotation"])
    cos, sin = math.cos(radians), math.sin(radians)
    if shape["type"] == "rect":
        half_w, half_h = shape["width"] / 2, shape["height"] / 2
        extent_x = abs(cos) * half_w + abs(sin) * half_h
        extent_y = abs(sin) * half_w + abs(cos) * half_h
        cx, cy = shape["x"] + half_w, shape["y"] + half_h
    else:
        rx, ry = shape["radius_x"], shape["radius_y"]
        extent_x = math.hypot(rx * cos, ry * sin)
        extent_y = math.hypot(rx * sin, ry * cos)
        cx, cy = shape["x"], shape["y"]
    return cx - extent_x, cy - extent_y, cx + extent_x, cy + extent_y


def _check_terrain(result, schema, width, height, solid_alpha, destructible_alpha, spawn_names):
    threshold = schema["collisionAlpha"]
    _check_partial_alpha(result, solid_alpha, "solid")
    _check_partial_alpha(result, destructible_alpha, "destructible")

    # One byte per pixel, 1 where it collides. ANDing the big integers counts the overlap fast.
    collides = bytes(1 if a >= threshold else 0 for a in range(256))
    solid = solid_alpha.translate(collides)
    destructible = destructible_alpha.translate(collides)
    overlap = (int.from_bytes(solid, "big") & int.from_bytes(destructible, "big")).bit_count()
    if overlap:
        result.warning(f"{overlap} pixels are in both solid and destructible, solid wins, so "
                       "they can't be blasted")

    body = schema["guides"]["body"]
    half_width, body_height = body["width"] // 2, body["height"]

    def blocked(x0, y0, x1, y1):
        # Pixels outside the map are empty, like in the game.
        for y in range(max(y0, 0), min(y1, height)):
            row = y * width
            for x in range(max(x0, 0), min(x1, width)):
                if solid[row + x] or destructible[row + x]:
                    return True
        return False

    for spawn, name in zip(result.manifest["spawn_points"], spawn_names):
        x, y = spawn["x"], spawn["y"]
        if blocked(x - half_width, y - body_height, x + half_width, y):
            result.error("the spawn is inside terrain", name)
        elif not blocked(x - half_width, y, x + half_width, y + 1):
            result.warning("the spawn is not standing on the ground", name)


def _check_partial_alpha(result, alpha, layer):
    transparent = alpha.count(0)
    visible = len(alpha) - transparent
    partial = visible - alpha.count(255)
    if visible and partial > visible * _PARTIAL_ALPHA_WARNING_FRACTION:
        result.warning(f"{partial} of {visible} {layer} pixels are semi-transparent; only alpha "
                       "of 50% or more collides, so what you see may not match what you hit")
