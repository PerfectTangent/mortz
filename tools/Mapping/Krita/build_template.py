"""Builds the Mortz map template in templates/mortz/. Run from the repository root:

PYTHONPATH=tools/Mapping/Krita PYTHONUNBUFFERED=1 kritarunner -s build_template
"""

import os

from mortz import document, guides
from mortz.qt import QPoint

# One screen. Authors paste or paint past the edges, then use Tools > Mortz > Fit canvas to terrain.
WIDTH, HEIGHT = 1280, 720
NAME = "mortz-map"
GRID = 32
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "templates", "mortz")

DIRECTORY = """[Desktop Entry]
Name=Mortz Maps
"""

DESKTOP = """[Desktop Entry]
Icon=template_web_design
Name=Mortz map
Type=Link
URL[$e]=.source/{file}
X-KDE-Hidden=false
"""


def build(app, width, height, path):
    doc = app.createDocument(width, height, "Mortz map", "RGBA", "U8", "", 72.0)
    doc.setBatchmode(True)
    root = doc.rootNode()
    # The default background layer's name is localised. It is the only child.
    for node in root.childNodes():
        node.remove()

    # Bottom to top.
    for name in ("Background", "Solid", "Destructible"):
        group = document.add_on_top(root, doc.createGroupLayer(name))
        document.add_on_top(group, doc.createNode(name.lower(), "paintlayer"))
    for name in ("Spawns", "Zones"):
        document.add_on_top(root, doc.createVectorLayer(name))
    guides.build(doc)

    grid = doc.gridConfig()
    grid.setSpacing(QPoint(GRID, GRID))
    grid.setSubdivision(4)
    grid.setSnap(True)
    grid.setVisible(True)
    doc.setGridConfig(grid)

    document.save_annotation(doc, {"version": document.ANNOTATION_VERSION})
    document.save_as(doc, path)
    doc.close()


def __main__(args):
    from krita import Krita

    app = Krita.instance()
    os.makedirs(os.path.join(OUT, ".source"), exist_ok=True)
    with open(os.path.join(OUT, ".directory"), "w", encoding="utf-8") as file:
        file.write(DIRECTORY)
    build(app, WIDTH, HEIGHT, os.path.join(OUT, ".source", f"{NAME}.kra"))
    with open(os.path.join(OUT, f"{NAME}.desktop"), "w", encoding="utf-8") as file:
        file.write(DESKTOP.format(file=f"{NAME}.kra"))
    print(f"wrote {NAME}")
