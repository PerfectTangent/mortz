"""What the Mortz docker and shortcuts do to a document. Errors the author has to fix are raised
as CommandError, with a message for them."""

import os
import re

from krita import Krita

from . import canvas, document, export, geometry, guides, schema
from .manifest import SPAWNS, ZONES
from .qt import QFileDialog, QPointF

SETTINGS_GROUP = "mortz"
EXPORT_ON_SAVE = "exportOnSave"
# What the official maps use, so new spawns look like the old ones.
SPAWN_COLOURS = {"blue": "#3c78ff", "red": "#ff3c3c"}
NO_TEAM_COLOUR = "#c8c8c8"
_FILL = re.compile(r' fill="[^"]*"')


class CommandError(Exception):
    pass


def check(doc):
    """Returns manifest.Result."""
    result, _ = export.check(doc)
    return result


def export_map(doc, window, ask):
    """Returns (manifest.Result, written folder or None), or None when there is no pack to
    export into: the author cancelled choosing one, or ask is False and none is known yet. An
    empty map id becomes the file name."""
    if not document.load_annotation(doc).get("id") and doc.fileName():
        default_id = os.path.splitext(os.path.basename(doc.fileName()))[0]
        document.edit_annotation(doc, lambda annotation: annotation.update(id=default_id))
    pack = pack_directory(doc, window, ask)
    if pack is None:
        return None
    try:
        return export.export(doc, pack)
    except (export.ExportError, OSError) as error:
        raise CommandError(str(error))


def pack_directory(doc, window, ask):
    """<root>/official/source/Maps/<pack>/<id>.kra exports to <root>/content/<pack>/. Anything
    else asks once per document (when ask) and remembers the answer."""
    path = os.path.abspath(doc.fileName()) if doc.fileName() else ""
    pack_source = os.path.dirname(path)
    root = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(pack_source))))
    if path and os.path.join(root, "official", "source", "Maps") == os.path.dirname(pack_source):
        return os.path.join(root, "content", os.path.basename(pack_source))

    app = Krita.instance()
    key = f"pack:{path}"
    remembered = app.readSetting(SETTINGS_GROUP, key, "") if path else ""
    if remembered and os.path.isdir(remembered):
        return remembered
    if not ask:
        return None
    chosen = QFileDialog.getExistingDirectory(window, "Content pack to export into")
    if chosen and path:
        app.writeSetting(SETTINGS_GROUP, key, chosen)
    return chosen or None


def export_on_save():
    return Krita.instance().readSetting(SETTINGS_GROUP, EXPORT_ON_SAVE, "false") == "true"


def set_export_on_save(enabled):
    Krita.instance().writeSetting(SETTINGS_GROUP, EXPORT_ON_SAVE, "true" if enabled else "false")


def is_map(doc):
    """Whether the document looks like a Mortz map, so saving other images never exports."""
    return bool(document.load_annotation(doc).get("id")) or bool(document.shape_layers(doc))


def fit_canvas(doc):
    try:
        canvas.fit_to_terrain(doc)
    except canvas.FitError as error:
        raise CommandError(str(error))


def rebuild_guides(doc):
    guides.build(doc)


def guides_to_selected_spawn(doc):
    selected = selected_shape(doc)
    if selected is None or selected[0] != SPAWNS:
        raise CommandError("Select a spawn on the Spawns layer first.")
    guides.build(doc, _geometry(doc, selected[1]).feet())


def add_spawn(doc):
    """Adds a player-sized spawn in the middle of the view and selects it. Returns its name."""
    layer = document.find_node(doc, SPAWNS)
    if layer is None or layer.type() != "vectorlayer":
        raise CommandError("The map has no Spawns vector layer.")
    x, y = _view_centre(doc)
    radius = schema.load()["guides"]["body"]["height"] / 2
    shapes = layer.addShapesFromSvg(
        f'<svg xmlns="http://www.w3.org/2000/svg" width="{doc.width()}" height="{doc.height()}">'
        f'<circle cx="{x}" cy="{y}" r="{radius}" fill="{NO_TEAM_COLOUR}" fill-opacity="0.6" '
        f'stroke="#000000" stroke-width="1"/></svg>')
    if not shapes:
        raise CommandError("Krita could not add the spawn.")
    document.name_new_shapes(doc)
    name = shapes[0].name()
    select_shape(doc, name)
    return name


def selected_shape(doc):
    """(layer name, shape) when exactly one zone or spawn is selected on the active layer."""
    active = doc.activeNode()
    if active is None or _active_view() is None:
        return None
    for layer_name, layer in document.shape_layers(doc):
        if layer.uniqueId() != active.uniqueId():
            continue
        selected = [shape for shape in layer.shapes() if shape.isSelected()]
        return (layer_name, selected[0]) if len(selected) == 1 else None
    return None


def select_shape(doc, name):
    """Makes the shape's layer active, selects only that shape and centres the view on it."""
    found = document.find_shape(doc, name)
    if found is None:
        return False
    _, layer, shape = found
    doc.setActiveNode(layer)
    view = _active_view()
    if view is None:
        # Selecting goes through the canvas, and Krita crashes without one.
        return False
    for other in layer.shapes():
        other.deselect()
    shape.select()
    # The selection tool shows the selection handles and lets the author drag it right away.
    action = Krita.instance().action("InteractionTool")
    if action is not None:
        action.trigger()
    try:
        g = _geometry(doc, shape)
        view.canvas().setPreferredCenter(QPointF(g.cx, g.cy))
    except CommandError:
        pass
    return True


def set_spawn_team(doc, name, team):
    """Stores the team and repaints the spawn in the team's colour."""
    def edit(annotation):
        entry = annotation.setdefault("spawns", {}).setdefault(name, {})
        if team:
            entry["team"] = team
        else:
            entry.pop("team", None)
        _drop_empty(annotation, "spawns", name)
    document.edit_annotation(doc, edit)
    _repaint(doc, name, SPAWN_COLOURS.get(team, NO_TEAM_COLOUR))


def _repaint(doc, name, colour):
    """Krita's API can't change a shape's fill, so this swaps in a copy with the new fill: same
    name, transform, stacking and selection. Leaves the shape alone if the copy lands anywhere
    else."""
    found = document.find_shape(doc, name)
    # Removing a shape goes through the canvas, and Krita crashes without one.
    if found is None or _active_view() is None:
        return
    _, layer, old = found
    svg, count = _FILL.subn(f' fill="{colour}"', old.toSvg(), count=1)
    if not count:
        return
    added = layer.addShapesFromSvg(f'<svg {geometry.NAMESPACES} width="{doc.width()}" '
                                   f'height="{doc.height()}">{svg}</svg>')
    if len(added) != 1:
        for shape in added:
            shape.remove()
        return
    new = added[0]
    new.setTransformation(old.transformation())
    new.setZIndex(old.zIndex())
    try:
        moved = _geometry(doc, new).feet() != _geometry(doc, old).feet()
    except CommandError:
        moved = True
    if moved:
        new.remove()
        return
    selected = old.isSelected()
    old.remove()
    new.setName(name)
    if selected:
        new.select()


def set_zone_tags(doc, name, tags):
    def edit(annotation):
        entry = annotation.setdefault("zones", {}).setdefault(name, {})
        if tags:
            entry["tags"] = tags
        else:
            entry.pop("tags", None)
        _drop_empty(annotation, "zones", name)
    document.edit_annotation(doc, edit)


def set_zone_effects(doc, name, effects):
    def edit(annotation):
        entry = annotation.setdefault("zones", {}).setdefault(name, {})
        if effects:
            entry["effects"] = effects
        else:
            entry.pop("effects", None)
        _drop_empty(annotation, "zones", name)
    document.edit_annotation(doc, edit)


def rename_zone(doc, old, new):
    """Renames the zone shape and moves its settings along."""
    if new == old:
        return
    if not new:
        raise CommandError("A zone needs a name.")
    if document.find_shape(doc, new) is not None:
        raise CommandError(f"There is already a shape named '{new}'.")
    found = document.find_shape(doc, old)
    if found is None or found[0] != ZONES:
        return
    found[2].setName(new)

    def edit(annotation):
        zones = annotation.get("zones") or {}
        if old in zones:
            zones[new] = zones.pop(old)
    document.edit_annotation(doc, edit)


def _drop_empty(annotation, section, name):
    if not annotation[section][name]:
        del annotation[section][name]
    if not annotation[section]:
        del annotation[section]


def _geometry(doc, shape):
    try:
        return geometry.read(shape.toSvg(), document.transform_of(shape), doc.xRes())
    except geometry.ShapeError as error:
        raise CommandError(str(error))


def _active_view():
    window = Krita.instance().activeWindow()
    return window.activeView() if window is not None else None


def _view_centre(doc):
    """The middle of what the author is looking at, in document pixels; the middle of the
    document when there is no view of it."""
    view = _active_view()
    if view is not None and view.document() is not None and \
            view.document().rootNode().uniqueId() == doc.rootNode().uniqueId():
        centre = view.canvas().preferredCenter()
        return round(centre.x()), round(centre.y())
    return doc.width() // 2, doc.height() // 2
