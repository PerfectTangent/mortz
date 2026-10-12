"""Reads and writes the parts of a Krita document a Mortz map is made of."""

import json
import re

from .manifest import SPAWNS, ZONES
from .qt import QCoreApplication

ANNOTATION = "mortz/map"
ANNOTATION_VERSION = 1

BACKGROUND = "background"
SOLID = "solid"
DESTRUCTIBLE = "destructible"
TERRAIN = (BACKGROUND, SOLID, DESTRUCTIBLE)

# New shapes get <prefix>_<n> on the first export, so the annotation can key them.
SHAPE_PREFIX = {ZONES: "zone", SPAWNS: "spawn"}
# Krita saves a shape without a name as shape<n>.
_KRITA_NAME = re.compile(r"shape\d+")


def find_node(doc, name):
    """Top-level node by name, case-insensitive. Names starting with '_' never match."""
    name = name.lower()
    for node in doc.rootNode().childNodes():
        if node.name().lower() == name and not node.name().startswith("_"):
            return node
    return None


def add_on_top(parent, node):
    children = parent.childNodes()
    parent.addChildNode(node, children[-1] if children else None)
    return node


def save_as(doc, path):
    """saveAs() quietly fails while added shapes still wait on the event loop (Krita 6.0).
    The events queue more image work, so wait for that after running them."""
    QCoreApplication.processEvents()
    doc.waitForDone()
    if not doc.saveAs(path):
        raise RuntimeError(f"Krita could not save {path}")


def load_annotation(doc):
    raw = bytes(doc.annotation(ANNOTATION) or b"")
    annotation = json.loads(raw) if raw else {}
    annotation.setdefault("version", ANNOTATION_VERSION)
    return annotation


def save_annotation(doc, annotation):
    data = json.dumps(annotation, indent=2, sort_keys=True).encode("utf-8")
    doc.setAnnotation(ANNOTATION, "Mortz map", data)


def name_new_shapes(doc):
    """Gives every zone and spawn without a name of its own a unique one. Returns True when it
    renamed any."""
    renamed = False
    for layer_name, layer in shape_layers(doc):
        prefix = SHAPE_PREFIX[layer_name]
        shapes = layer.shapes()
        unnamed = [shape for shape in shapes if is_unnamed(shape)]
        taken = {shape.name() for shape in shapes if not is_unnamed(shape)}
        n = 1
        for shape in unnamed:
            while f"{prefix}_{n}" in taken:
                n += 1
            shape.setName(f"{prefix}_{n}")
            taken.add(shape.name())
            renamed = True
    return renamed


def is_unnamed(shape):
    return not shape.name() or _KRITA_NAME.fullmatch(shape.name()) is not None


def read_shapes(doc):
    """Zone and spawn shapes as raw SVG and transform (points), for geometry.read."""
    shapes = []
    for layer_name, layer in shape_layers(doc):
        for shape in layer.shapes():
            # Includes the layer's offset, which Krita sets when the canvas is resized.
            shapes.append({
                "layer": layer_name,
                "name": shape.name(),
                "svg": shape.toSvg(),
                "transform": transform_of(shape),
            })
    return shapes



def shape_layers(doc):
    """The Zones and Spawns vector layers that exist, as (layer name, node)."""
    layers = []
    for layer_name in SHAPE_PREFIX:
        layer = find_node(doc, layer_name)
        if layer is not None and layer.type() == "vectorlayer":
            layers.append((layer_name, layer))
    return layers


def find_shape(doc, name):
    """(layer name, layer, shape) of the zone or spawn called name, or None."""
    for layer_name, layer in shape_layers(doc):
        for shape in layer.shapes():
            if shape.name() == name:
                return layer_name, layer, shape
    return None


def transform_of(shape):
    """[m11, m12, m21, m22, dx, dy] of the shape's absolute transform, for geometry.read."""
    t = shape.absoluteTransformation()
    return [t.m11(), t.m12(), t.m21(), t.m22(), t.dx(), t.dy()]


def edit_annotation(doc, edit):
    """Loads the annotation, lets edit(annotation) change it, saves it and marks the document
    modified, so the change isn't lost on close."""
    annotation = load_annotation(doc)
    edit(annotation)
    save_annotation(doc, annotation)
    doc.setModified(True)
