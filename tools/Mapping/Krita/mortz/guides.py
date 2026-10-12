"""The _Guides group: a viewport frame and reach guides sized from schema.json."""

from . import document, schema

GROUP = "_Guides"
VIEWPORT = "viewport"
REACH = "reach"
PLAYER = "player"
BODY = "body"

VIEWPORT_COLOUR = "#00b4ff"
REACH_COLOUR = "#ff3c78"
BLAST_COLOUR = "#ff9a00"


def build(doc, feet=None):
    """Replaces the _Guides group with a fresh one, drawn on top, with the player's feet at feet
    (document pixels; default: where the viewport puts them). Each guide gets its own layer so
    authors can hide it on its own. The body and what it can reach (jumps, rope) share the player
    group, so the move tool drags them together. Layers keep the visibility they had. Only the
    viewport frame is locked."""
    root = doc.rootNode()
    visible = {}
    for node in root.childNodes():
        if node.name() == GROUP:
            visible = {child.name(): child.visible() for child in _descendants(node)}
            node.remove()

    guides = schema.load()["guides"]
    view = guides["viewport"]
    if feet is None:
        feet = (view["width"] / 2, view["height"] * 0.75)
    group = document.add_on_top(root, doc.createGroupLayer(GROUP))
    reach = document.add_on_top(group, doc.createGroupLayer(REACH))
    loose, attached = _reach(guides, feet)
    _add_layers(doc, reach, loose)
    _add_layers(doc, document.add_on_top(reach, doc.createGroupLayer(PLAYER)), attached)
    viewport = document.add_on_top(group, doc.createVectorLayer(VIEWPORT))
    viewport.addShapesFromSvg(_svg(doc, _viewport(guides, feet)))
    viewport.setLocked(True)
    for node in _descendants(group):
        node.setVisible(visible.get(node.name(), True))
    return group


def _add_layers(doc, parent, parts):
    for name, shapes in parts:
        layer = document.add_on_top(parent, doc.createVectorLayer(name))
        layer.addShapesFromSvg(_svg(doc, shapes))


def find(doc):
    for node in doc.rootNode().childNodes():
        if node.name() == GROUP:
            return node
    return None


def layers(doc):
    """The viewport and reach guide layers, top to bottom, as (name, node)."""
    group = find(doc)
    if group is None:
        return []
    return [(node.name(), node) for node in reversed(_descendants(group))
            if node.type() == "vectorlayer"]


def body(doc):
    """The player body guide layer, or None."""
    return next((node for name, node in layers(doc) if name == BODY), None)


def _descendants(node):
    nodes = []
    for child in node.childNodes():
        nodes.append(child)
        nodes.extend(_descendants(child))
    return nodes


def _viewport(guides, feet):
    """The camera framing a player standing at feet."""
    w, h = guides["viewport"]["width"], guides["viewport"]["height"]
    x, y = feet[0] - w / 2, feet[1] - h * 0.75
    return [
        _rect(x, y, w, h, VIEWPORT_COLOUR, width=3),
        _label(x + 8, y + 24, f"viewport {w}x{h}", VIEWPORT_COLOUR),
    ]


def _reach(guides, feet):
    """A player standing at feet, with how far they can get, as (layer name, shapes) pairs,
    bottom to top: (guides that move on their own, guides attached to the body)."""
    body_w, body_h = guides["body"]["width"], guides["body"]["height"]
    feet_x, feet_y = feet
    centre_y = feet_y - body_h / 2

    blast_x = feet_x - 200
    blast_y = feet_y - guides["blastRadius"]
    loose = [
        ("blast", [
            _circle(blast_x, blast_y, guides["blastRadius"], BLAST_COLOUR),
            _circle(blast_x, blast_y, guides["blastCoreRadius"], BLAST_COLOUR, dashed=True),
            _label(blast_x - 40, blast_y - guides["blastRadius"] - 8, "blast", BLAST_COLOUR),
        ]),
        ("mortar", [
            _line(feet_x, feet_y, feet_x + guides["mortarRange"], feet_y, BLAST_COLOUR,
                  dashed=True),
            _label(feet_x + guides["mortarRange"] - 200, feet_y - 8,
                   f"flat mortar range {guides['mortarRange']:g}", BLAST_COLOUR),
        ]),
    ]

    attached = [("rope", [
        _circle(feet_x, centre_y, guides["rope"], REACH_COLOUR, dashed=True),
        _label(feet_x + 8, centre_y - guides["rope"] + 20, f"rope {guides['rope']:g}",
               REACH_COLOUR),
    ])]
    # Feet height at the top of each jump.
    for key, label in (("jump", "jump"), ("doubleJump", "double jump"), ("wallJump", "wall jump")):
        y = feet_y - guides[key]
        attached.append((label, [
            _line(feet_x - 80, y, feet_x + 80, y, REACH_COLOUR),
            _label(feet_x + 88, y + 5, f"{label} {guides[key]:g}", REACH_COLOUR),
        ]))
    attached.append((BODY, [
        _rect(feet_x - body_w / 2, feet_y - body_h, body_w, body_h, REACH_COLOUR, width=2),
    ]))
    return loose, attached


def _svg(doc, shapes):
    # SVG user units are document pixels for addShapesFromSvg.
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{doc.width()}" '
            f'height="{doc.height()}">{"".join(shapes)}</svg>')


def _stroke(colour, width=1.5, dashed=False):
    dash = ' stroke-dasharray="8,6"' if dashed else ""
    return f'fill="none" stroke="{colour}" stroke-width="{width}"{dash}'


def _rect(x, y, w, h, colour, width=1.5):
    return f'<rect x="{x}" y="{y}" width="{w}" height="{h}" {_stroke(colour, width)}/>'


def _circle(x, y, r, colour, dashed=False):
    return f'<circle cx="{x}" cy="{y}" r="{r}" {_stroke(colour, dashed=dashed)}/>'


def _line(x1, y1, x2, y2, colour, dashed=False):
    return f'<path d="M {x1} {y1} L {x2} {y2}" {_stroke(colour, dashed=dashed)}/>'


def _label(x, y, text, colour):
    return (f'<text x="{x}" y="{y}" fill="{colour}" font-family="sans-serif" '
            f'font-size="14">{text}</text>')
