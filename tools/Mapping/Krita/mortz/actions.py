"""The Mortz actions. Shortcuts, the Tools > Mortz menu and the docker's buttons all run these.
Results go to the visible Mortz dockers, or to a message box when none is open."""

import os
import weakref

from krita import Krita

from . import commands
from .manifest import ERROR, WARNING
from .qt import QMessageBox

TITLE = "Mortz"
# Every MortzDocker adds itself, so results reach all of them.
dockers = weakref.WeakSet()


def check():
    doc = _document()
    if doc is not None:
        _run(doc, lambda: _report(doc, commands.check(doc), exported=None))


def export():
    doc = _document()
    if doc is not None:
        _run(doc, lambda: _export(doc, ask=True))


def fit_canvas():
    doc = _document()
    if doc is not None:
        _run(doc, lambda: commands.fit_canvas(doc))


def add_spawn():
    doc = _document()
    if doc is not None:
        _run(doc, lambda: commands.add_spawn(doc))


def guides_to_spawn():
    doc = _document()
    if doc is not None:
        _run(doc, lambda: commands.guides_to_selected_spawn(doc))


def rebuild_guides():
    doc = _document()
    if doc is not None:
        _run(doc, lambda: commands.rebuild_guides(doc))


# (name, menu text, handler). Names match mortz.action, which holds the default shortcuts.
ALL = (
    ("mortz_check_map", "Check Mortz map", check),
    ("mortz_export_map", "Export Mortz map", export),
    ("mortz_fit_canvas", "Fit canvas to terrain", fit_canvas),
    ("mortz_add_spawn", "Add spawn", add_spawn),
    ("mortz_guides_to_spawn", "Move reach guides to selected spawn", guides_to_spawn),
    ("mortz_rebuild_guides", "Rebuild guides", rebuild_guides),
)


def on_image_saved(file_name):
    """Exports a saved map when the author turned on export on save. Never asks for a pack."""
    if not commands.export_on_save():
        return
    doc = next((doc for doc in Krita.instance().documents() if doc.fileName() == file_name), None)
    if doc is not None and commands.is_map(doc):
        _run(doc, lambda: _export(doc, ask=False))


def _export(doc, ask):
    exported = commands.export_map(doc, _window(), ask)
    if exported is None:
        if not ask:
            _tell(doc, "Not exported on save: export once by hand to choose its content pack.",
                  [])
        return
    result, written = exported
    _report(doc, result, exported=written or "")


def _report(doc, result, exported):
    """exported: None for a check, the folder for an export, "" for an export that failed."""
    errors = sum(1 for issue in result.issues if issue["severity"] == ERROR)
    warnings = sum(1 for issue in result.issues if issue["severity"] == WARNING)
    counts = f"{errors} error{'s' * (errors != 1)}, {warnings} warning{'s' * (warnings != 1)}"
    if exported:
        summary = f"Exported to {exported} ({counts})."
    elif exported == "":
        summary = f"Not exported: {counts}."
    else:
        summary = f"Checked: {counts}." if result.issues else "Checked: no issues."
    _tell(doc, summary, result.issues)


def _run(doc, command):
    try:
        command()
    except commands.CommandError as error:
        _tell(doc, str(error), [])


def _tell(doc, summary, issues):
    title = os.path.basename(doc.fileName()) if doc.fileName() else "Untitled"
    shown = False
    for docker in list(dockers):
        if docker.isVisible():
            docker.show_report(title, summary, issues)
            shown = True
    if shown:
        return
    lines = [summary] + [issue_text(issue) for issue in issues]
    QMessageBox.information(_window(), TITLE, "\n".join(lines))


def issue_text(issue):
    where = f"[{issue['shape']}] " if issue["shape"] else ""
    return f"{issue['severity']}: {where}{issue['message']}"


def _document():
    doc = Krita.instance().activeDocument()
    if doc is None:
        QMessageBox.information(_window(), TITLE, "Open a map document first.")
    return doc


def _window():
    window = Krita.instance().activeWindow()
    return window.qwindow() if window else None
