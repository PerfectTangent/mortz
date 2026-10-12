"""Exports the document as a map package into a content pack."""

import os

from . import document, manifest, package, schema
from .qt import QBuffer, QByteArray, QImage, QIODevice


class ExportError(Exception):
    pass


def check(doc):
    """Validates the document without writing anything. Returns (manifest.Result, pixels by
    terrain layer). Names unnamed shapes first, so the document may change."""
    document.name_new_shapes(doc)
    width, height = doc.width(), doc.height()
    # Group projections read back as zeros until the projection has been rebuilt.
    doc.refreshProjection()
    doc.waitForDone()
    pixels = {name: _pixels(document.find_node(doc, name), width, height)
              for name in document.TERRAIN}

    result = manifest.build(schema.load(), width, height, doc.xRes(),
                            document.load_annotation(doc), document.read_shapes(doc),
                            pixels[document.SOLID][3::4], pixels[document.DESTRUCTIBLE][3::4])
    return result, pixels


def export(doc, pack_directory):
    """Returns (manifest.Result, written folder or None). Writes nothing when there are errors."""
    if not os.path.isfile(os.path.join(pack_directory, "content_pack.toml")):
        raise ExportError(f"'{pack_directory}' is not a content pack (no content_pack.toml)")

    result, pixels = check(doc)
    if result.has_errors:
        return result, None

    width, height = doc.width(), doc.height()
    files = {f"{name}.png": _png(data, width, height) for name, data in pixels.items()}
    files["map.toml"] = package.to_toml(result.manifest).encode("utf-8")
    written = package.write(os.path.join(pack_directory, "maps"),
                            document.load_annotation(doc)["id"], files)
    return result, written


def _pixels(node, width, height):
    """BGRA, straight alpha. A missing layer is fully transparent."""
    if node is None:
        return bytes(width * height * 4)
    return bytes(node.projectionPixelData(0, 0, width, height))


def _png(data, width, height):
    # BGRA with straight alpha is Format_ARGB32's byte layout on little-endian machines.
    image = QImage(data, width, height, width * 4, QImage.Format.Format_ARGB32)
    array = QByteArray()
    buffer = QBuffer(array)
    buffer.open(QIODevice.OpenModeFlag.WriteOnly)
    if not image.save(buffer, "PNG"):
        raise ExportError("Qt could not encode a PNG")
    buffer.close()
    return bytes(array)
