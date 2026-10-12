"""Krita vector shapes (toSvg() + transformation()) as map zones and spawns. No Krita imports."""

import math
import re
import xml.etree.ElementTree as ET

RECT = "rect"
ELLIPSE = "ellipse"

# toSvg() output can use prefixes it doesn't declare itself.
NAMESPACES = (
    'xmlns="http://www.w3.org/2000/svg" '
    'xmlns:krita="http://krita.org/namespaces/svg/krita" '
    'xmlns:sodipodi="http://sodipodi.sourceforge.net/DTD/sodipodi-0.dtd" '
    'xmlns:inkscape="http://www.inkscape.org/namespaces/inkscape" '
    'xmlns:xlink="http://www.w3.org/1999/xlink"'
)
_LEADING_NUMBER = re.compile(r"\s*[-+]?(\d+\.?\d*|\.\d+)([eE][-+]?\d+)?")
_SKEW_TOLERANCE = 1e-3
_ROTATION_PRECISION = 1e-3


class ShapeError(Exception):
    """The shape can't be used. The message is for the author."""


class Geometry:
    """A shape in document pixels: centre, unrotated size, clockwise rotation in degrees."""

    def __init__(self, kind, cx, cy, width, height, rotation):
        self.kind = kind
        self.cx, self.cy = cx, cy
        self.width, self.height = width, height
        self.rotation = rotation

    def feet(self):
        """Bottom-centre of the shape, which is where a spawn's feet go."""
        radians = math.radians(self.rotation)
        return (round_half_away(self.cx - self.height / 2 * math.sin(radians)),
                round_half_away(self.cy + self.height / 2 * math.cos(radians)))

    def zone_shape(self):
        """The [zones.shape] table for map.toml."""
        if self.kind == RECT:
            width, height = round_half_away(self.width), round_half_away(self.height)
            return {"type": "rect",
                    "x": round_half_away(self.cx - width / 2),
                    "y": round_half_away(self.cy - height / 2),
                    "width": width, "height": height, "rotation": float(self.rotation)}
        x, y = round_half_away(self.cx), round_half_away(self.cy)
        radius_x, radius_y = round_half_away(self.width / 2), round_half_away(self.height / 2)
        # Rotation means nothing for equal radii, so a circle loses nothing.
        if radius_x == radius_y:
            return {"type": "circle", "x": x, "y": y, "radius": radius_x}
        return {"type": "ellipse", "x": x, "y": y, "radius_x": radius_x, "radius_y": radius_y,
                "rotation": float(self.rotation)}


def read(svg, transform, x_res):
    """svg is shape.toSvg(), transform [m11, m12, m21, m22, dx, dy] of shape.transformation(),
    both in points."""
    if len(transform) != 6 or not all(math.isfinite(v) for v in transform):
        raise ShapeError("the shape transform must be 6 finite numbers")
    kind, left, top, width, height = _local_box(_parse(svg))

    # Krita's shape API is in points; scale to document pixels after the shape transform.
    m11, m12, m21, m22, dx, dy = (v * x_res / 72 for v in transform)
    x_axis = math.hypot(m11, m12)
    y_axis = math.hypot(m21, m22)
    if x_axis == 0 or y_axis == 0:
        raise ShapeError("the shape is squashed flat")
    if abs(m11 * m21 + m12 * m22) / (x_axis * y_axis) > _SKEW_TOLERANCE:
        raise ShapeError("the shape is skewed, reset its transform and redraw it")
    if m11 * m22 - m12 * m21 < 0:
        raise ShapeError("the shape is mirrored; flip it back")

    local_x, local_y = left + width / 2, top + height / 2
    return Geometry(kind,
                    m11 * local_x + m21 * local_y + dx,
                    m12 * local_x + m22 * local_y + dy,
                    width * x_axis, height * y_axis,
                    _normalize_rotation(math.degrees(math.atan2(m12, m11))))


def round_half_away(value):
    return int(math.copysign(math.floor(abs(value) + 0.5), value))


def _parse(svg):
    try:
        wrapper = ET.fromstring(f"<wrapper {NAMESPACES}>{svg}</wrapper>")
    except ET.ParseError as error:
        raise ShapeError(f"the shape's SVG doesn't parse: {error}")
    if len(wrapper) == 0:
        raise ShapeError("the shape has no SVG element")
    return wrapper[0]


def _local_box(element):
    tag = element.tag.split("}")[-1]
    if tag == "rect":
        return (RECT, _number(element, "x", 0), _number(element, "y", 0),
                _positive(element, "width"), _positive(element, "height"))
    if tag == "ellipse":
        rx, ry = _positive(element, "rx"), _positive(element, "ry")
        return (ELLIPSE, _number(element, "cx", 0) - rx, _number(element, "cy", 0) - ry,
                2 * rx, 2 * ry)
    if tag == "circle":
        r = _positive(element, "r")
        return (ELLIPSE, _number(element, "cx", 0) - r, _number(element, "cy", 0) - r, 2 * r, 2 * r)
    if tag == "path":
        raise ShapeError("the shape became a path (node-edited, converted, or a pie/arc), "
                         "delete it and redraw it with the rectangle or ellipse tool")
    raise ShapeError(f"'{tag}' shapes aren't supported; use the rectangle or ellipse tool")


def _positive(element, attribute):
    value = _number(element, attribute, None)
    if not value > 0:
        raise ShapeError(f"the shape's {attribute} must be positive")
    return value


def _number(element, attribute, fallback):
    text = element.get(attribute)
    if text is None:
        if fallback is None:
            raise ShapeError(f"the shape has no {attribute}")
        return fallback
    # Krita may append a unit. The number is in the shape's local units either way.
    match = _LEADING_NUMBER.match(text)
    if not match:
        raise ShapeError(f"the shape's {attribute} '{text}' is not a number")
    return float(match.group(0))


def _normalize_rotation(degrees):
    degrees = math.fmod(degrees, 360)
    if degrees <= -180:
        degrees += 360
    elif degrees > 180:
        degrees -= 360
    degrees = round(degrees / _ROTATION_PRECISION) * _ROTATION_PRECISION
    # Also turns -0 into 0, so it doesn't print as "-0.0".
    return 0.0 if abs(degrees) < _ROTATION_PRECISION else round(degrees, 3)
