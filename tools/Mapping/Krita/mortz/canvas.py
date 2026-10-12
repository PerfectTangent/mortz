"""Fit the canvas to the terrain, so authors paste or paint and never type a map size."""

from . import document, schema
from .qt import QCoreApplication


class FitError(Exception):
    pass


def fit_to_terrain(doc):
    """Resizes the canvas to the bounds of everything in Background, Solid and Destructible,
    including pixels outside the current canvas. Returns the new (width, height)."""
    doc.refreshProjection()
    doc.waitForDone()
    bounds = None
    for name in document.TERRAIN:
        node = document.find_node(doc, name)
        if node is None:
            continue
        rect = node.bounds()
        if rect.isEmpty():
            continue
        bounds = rect if bounds is None else bounds.united(rect)
    if bounds is None:
        raise FitError("Background, Solid and Destructible are empty; nothing to fit to")

    limits = schema.load()["limits"]
    if bounds.width() > limits["maxSize"] or bounds.height() > limits["maxSize"]:
        raise FitError(f"the terrain is {bounds.width()}x{bounds.height()}; maps can be at most "
                       f"{limits['maxSize']}x{limits['maxSize']}")
    # Too small: grow around the terrain so it stays centred.
    width = max(bounds.width(), limits["minWidth"])
    height = max(bounds.height(), limits["minHeight"])
    x = bounds.x() - (width - bounds.width()) // 2
    y = bounds.y() - (height - bounds.height()) // 2
    doc.resizeImage(x, y, width, height)
    # Shapes only pick up their layer's new offset once the events have run.
    QCoreApplication.processEvents()
    doc.waitForDone()
    return width, height
