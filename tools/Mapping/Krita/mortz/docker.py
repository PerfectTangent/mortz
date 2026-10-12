"""The Mortz map docker (Settings > Dockers > Mortz map): map settings, the selected spawn or
zone, guides, and check and export results."""

import os

from krita import DockWidget, Krita

from . import actions, commands, document, guides, schema
from .manifest import ERROR, SPAWNS, ZONES
from .qt import (QAbstractItemView, QBrush, QCheckBox, QColor, QComboBox, QDoubleSpinBox,
                 QFormLayout, QGridLayout, QGroupBox, QHBoxLayout, QHeaderView, QLabel,
                 QLineEdit, QListWidget, QListWidgetItem, QPushButton, QScrollArea,
                 QSignalBlocker, QSpinBox, QStackedWidget, Qt, QTableWidget, QTimer, QToolButton,
                 QVBoxLayout, QWidget)

TITLE = "Mortz map"
# Krita has no signal for shape selection, so the docker looks this often while it's visible.
POLL_MS = 250
NO_TEAM = "(none)"
ERROR_COLOUR = "#e05050"
WARNING_COLOUR = "#e0a030"

# Selected shape pages.
NOTHING, SPAWN, ZONE = range(3)
# Effects table columns.
STAT, OP, VALUE, REMOVE = range(4)


class MortzDocker(DockWidget):
    def __init__(self):
        super().__init__()
        self.setWindowTitle(TITLE)
        self._schema = schema.load()
        self._doc_key = None
        self._shape = None  # (layer name, shape name) shown in the selected shape section
        self._guide_names = None
        self._build()
        self._timer = QTimer(self)
        self._timer.timeout.connect(self._poll)
        self._timer.start(POLL_MS)
        actions.dockers.add(self)

    def canvasChanged(self, canvas):
        self._doc_key = None
        self._poll()

    def show_report(self, title, summary, issues):
        self.summary.setText(f"{title}: {summary}")
        self.issues.clear()
        for issue in issues:
            item = QListWidgetItem(actions.issue_text(issue))
            item.setData(Qt.ItemDataRole.UserRole, issue["shape"] or "")
            colour = ERROR_COLOUR if issue["severity"] == ERROR else WARNING_COLOUR
            item.setForeground(QBrush(QColor(colour)))
            if issue["shape"]:
                item.setToolTip("Click to select the shape")
            self.issues.addItem(item)

    # Layout.

    def _build(self):
        root = QWidget()
        layout = QVBoxLayout(root)

        row = QHBoxLayout()
        for text, handler in (("Check", actions.check), ("Export", actions.export),
                              ("Fit canvas", actions.fit_canvas)):
            row.addWidget(_button(text, handler))
        layout.addLayout(row)
        self.export_on_save = QCheckBox("Export on save")
        self.export_on_save.setToolTip("Export the map every time it's saved")
        self.export_on_save.setChecked(commands.export_on_save())
        self.export_on_save.toggled.connect(commands.set_export_on_save)
        layout.addWidget(self.export_on_save)

        self.map_group = QGroupBox("Map")
        form = QFormLayout(self.map_group)
        self.map_id = QLineEdit()
        self.map_id.setToolTip("Lowercase letters, digits, '_' and '-'. Empty: the file name")
        self.map_name = QLineEdit()
        self.players = QSpinBox()
        limits = self._schema["limits"]
        self.players.setRange(limits["minPlayers"], limits["maxPlayers"])
        self.map_id.editingFinished.connect(self._commit_map)
        self.map_name.editingFinished.connect(self._commit_map)
        self.players.valueChanged.connect(self._commit_map)
        form.addRow("Id", self.map_id)
        form.addRow("Name", self.map_name)
        form.addRow("Players", self.players)
        layout.addWidget(self.map_group)

        self.shape_group = QGroupBox("Selected shape")
        self.pages = QStackedWidget()
        self.pages.addWidget(self._build_nothing_page())
        self.pages.addWidget(self._build_spawn_page())
        self.pages.addWidget(self._build_zone_page())
        QVBoxLayout(self.shape_group).addWidget(self.pages)
        layout.addWidget(self.shape_group)

        self.guides_group = QGroupBox("Guides")
        guides_layout = QVBoxLayout(self.guides_group)
        row = QHBoxLayout()
        row.addWidget(_button("Add spawn", actions.add_spawn,
                              "Add a spawn in the middle of the view"))
        row.addWidget(_button("To spawn", actions.guides_to_spawn,
                              "Move the reach guides to the selected spawn"))
        row.addWidget(_button("Rebuild", actions.rebuild_guides,
                              "Redraw the guides from the current game values"))
        guides_layout.addLayout(row)
        self.guide_boxes = QGridLayout()
        guides_layout.addLayout(self.guide_boxes)
        layout.addWidget(self.guides_group)

        issues_group = QGroupBox("Issues")
        issues_layout = QVBoxLayout(issues_group)
        self.summary = QLabel("Check or export the map to see its issues.")
        self.summary.setWordWrap(True)
        self.issues = QListWidget()
        self.issues.setWordWrap(True)
        self.issues.itemClicked.connect(self._select_issue_shape)
        issues_layout.addWidget(self.summary)
        issues_layout.addWidget(self.issues)
        layout.addWidget(issues_group)
        layout.addStretch()

        scroll = QScrollArea()
        scroll.setWidgetResizable(True)
        scroll.setWidget(root)
        self.setWidget(scroll)

    def _build_nothing_page(self):
        label = QLabel("Select one spawn or zone with the shape selection tool, on the Spawns "
                       "or Zones layer.")
        label.setWordWrap(True)
        return label

    def _build_spawn_page(self):
        page = QWidget()
        form = QFormLayout(page)
        self.spawn_name = QLabel()
        self.team = QComboBox()
        self.team.addItems([NO_TEAM] + self._schema["teams"])
        self.team.currentIndexChanged.connect(self._commit_team)
        form.addRow("Spawn", self.spawn_name)
        form.addRow("Team", self.team)
        return page

    def _build_zone_page(self):
        page = QWidget()
        layout = QVBoxLayout(page)
        form = QFormLayout()
        self.zone_name = QLineEdit()
        self.zone_name.setToolTip("Lowercase letters, digits, '_' and '-'")
        self.zone_name.editingFinished.connect(self._commit_zone_name)
        self.tags = QLineEdit()
        self.tags.setPlaceholderText("comma-separated")
        self.tags.editingFinished.connect(self._commit_tags)
        form.addRow("Zone", self.zone_name)
        form.addRow("Tags", self.tags)
        layout.addLayout(form)

        self.effects = QTableWidget(0, 4)
        self.effects.setHorizontalHeaderLabels(["Stat", "Op", "Value", ""])
        self.effects.verticalHeader().setVisible(False)
        self.effects.setSelectionMode(QAbstractItemView.SelectionMode.NoSelection)
        header = self.effects.horizontalHeader()
        header.setSectionResizeMode(STAT, QHeaderView.ResizeMode.Stretch)
        for column in (OP, VALUE, REMOVE):
            header.setSectionResizeMode(column, QHeaderView.ResizeMode.ResizeToContents)
        self.effects.setMinimumHeight(120)
        layout.addWidget(QLabel("Effects"))
        layout.addWidget(self.effects)
        layout.addWidget(_button("Add effect", self._add_effect))
        return page

    # Following the document and the selection.

    def _poll(self):
        if not self.isVisible():
            return
        doc = Krita.instance().activeDocument()
        key = doc.rootNode().uniqueId().toString() if doc is not None else None
        if key != self._doc_key:
            self._doc_key = key
            self._shape = None
            self._guide_names = None
            self._load_map(doc)
        for group in (self.map_group, self.shape_group, self.guides_group):
            group.setEnabled(doc is not None)
        if doc is None:
            self.pages.setCurrentIndex(NOTHING)
            return
        self._sync_guides(doc)

        selected = commands.selected_shape(doc)
        if selected is not None and document.is_unnamed(selected[1]):
            # Settings are keyed by shape name, so it needs one before they can be edited.
            document.name_new_shapes(doc)
        shape = (selected[0], selected[1].name()) if selected is not None else None
        if shape != self._shape:
            self._shape = shape
            self._load_shape(doc)

    def _load_map(self, doc):
        annotation = document.load_annotation(doc) if doc is not None else {}
        default_id = os.path.splitext(os.path.basename(doc.fileName()))[0] \
            if doc is not None and doc.fileName() else ""
        with QSignalBlocker(self.map_id), QSignalBlocker(self.map_name), \
                QSignalBlocker(self.players):
            self.map_id.setText(annotation.get("id") or "")
            self.map_id.setPlaceholderText(default_id)
            self.map_name.setText(annotation.get("name") or "")
            self.players.setValue(annotation.get("suggestedPlayers") or 2)

    def _load_shape(self, doc):
        if self._shape is None:
            self.pages.setCurrentIndex(NOTHING)
            return
        layer_name, name = self._shape
        annotation = document.load_annotation(doc)
        if layer_name == SPAWNS:
            entry = (annotation.get("spawns") or {}).get(name) or {}
            self.spawn_name.setText(name)
            with QSignalBlocker(self.team):
                team = entry.get("team") or NO_TEAM
                if self.team.findText(team) < 0:
                    self.team.addItem(team)
                self.team.setCurrentText(team)
            self.pages.setCurrentIndex(SPAWN)
        else:
            entry = (annotation.get("zones") or {}).get(name) or {}
            self.zone_name.setText(name)
            self.tags.setText(", ".join(entry.get("tags") or []))
            self.effects.setRowCount(0)
            for effect in entry.get("effects") or []:
                self._append_effect_row(effect)
            self.pages.setCurrentIndex(ZONE)

    def _sync_guides(self, doc):
        layers = guides.layers(doc)
        names = [name for name, _ in layers]
        if names != self._guide_names:
            self._guide_names = names
            while self.guide_boxes.count():
                self.guide_boxes.takeAt(0).widget().deleteLater()
            if not names:
                self.guide_boxes.addWidget(QLabel("No guides. Rebuild adds them."), 0, 0)
            for i, name in enumerate(names):
                box = QCheckBox(name)
                box.toggled.connect(lambda checked, guide=name: self._show_guide(guide, checked))
                self.guide_boxes.addWidget(box, i // 2, i % 2)
        for i, (_, node) in enumerate(layers):
            box = self.guide_boxes.itemAt(i).widget()
            if box.isChecked() != node.visible():
                with QSignalBlocker(box):
                    box.setChecked(node.visible())

    # Edits.

    def _commit_map(self):
        doc = Krita.instance().activeDocument()
        if doc is None:
            return
        values = {"id": self.map_id.text().strip(), "name": self.map_name.text().strip(),
                  "suggestedPlayers": self.players.value()}
        annotation = document.load_annotation(doc)
        if all(annotation.get(key) == value for key, value in values.items()):
            return
        document.edit_annotation(doc, lambda annotation: annotation.update(values))

    def _commit_team(self):
        doc = Krita.instance().activeDocument()
        if doc is None or self._shape is None or self._shape[0] != SPAWNS:
            return
        team = self.team.currentText()
        commands.set_spawn_team(doc, self._shape[1], None if team == NO_TEAM else team)

    def _commit_zone_name(self):
        doc = Krita.instance().activeDocument()
        if doc is None or self._shape is None or self._shape[0] != ZONES:
            return
        new = self.zone_name.text().strip()
        try:
            commands.rename_zone(doc, self._shape[1], new)
        except commands.CommandError as error:
            self.summary.setText(str(error))
            self.zone_name.setText(self._shape[1])
            return
        self._shape = (ZONES, new)

    def _commit_tags(self):
        doc = Krita.instance().activeDocument()
        if doc is None or self._shape is None or self._shape[0] != ZONES:
            return
        tags = [tag.strip() for tag in self.tags.text().split(",") if tag.strip()]
        commands.set_zone_tags(doc, self._shape[1], tags)

    def _commit_effects(self):
        doc = Krita.instance().activeDocument()
        if doc is None or self._shape is None or self._shape[0] != ZONES:
            return
        effects = [{"stat": self.effects.cellWidget(row, STAT).currentText(),
                    "op": self.effects.cellWidget(row, OP).currentText(),
                    "value": self.effects.cellWidget(row, VALUE).value()}
                   for row in range(self.effects.rowCount())]
        commands.set_zone_effects(doc, self._shape[1], effects)

    def _add_effect(self):
        self._append_effect_row({"stat": self._schema["stats"][0], "op": "mul", "value": 1.0})
        self._commit_effects()

    def _append_effect_row(self, effect):
        row = self.effects.rowCount()
        self.effects.insertRow(row)
        stat = _combo(self._schema["stats"], effect.get("stat"))
        op = _combo(self._schema["ops"], effect.get("op"))
        value = QDoubleSpinBox()
        value.setRange(-1e6, 1e6)
        value.setDecimals(3)
        raw = effect.get("value")
        value.setValue(raw if isinstance(raw, (int, float)) else 0.0)
        remove = QToolButton()
        remove.setText("✕")
        remove.setToolTip("Remove this effect")
        stat.currentIndexChanged.connect(self._commit_effects)
        op.currentIndexChanged.connect(self._commit_effects)
        value.editingFinished.connect(self._commit_effects)
        remove.clicked.connect(lambda: self._remove_effect(remove))
        for column, widget in ((STAT, stat), (OP, op), (VALUE, value), (REMOVE, remove)):
            self.effects.setCellWidget(row, column, widget)

    def _remove_effect(self, button):
        for row in range(self.effects.rowCount()):
            if self.effects.cellWidget(row, REMOVE) is button:
                self.effects.removeRow(row)
                break
        self._commit_effects()

    def _show_guide(self, name, visible):
        doc = Krita.instance().activeDocument()
        if doc is None:
            return
        for guide, node in guides.layers(doc):
            if guide == name:
                node.setVisible(visible)
        doc.refreshProjection()

    def _select_issue_shape(self, item):
        name = item.data(Qt.ItemDataRole.UserRole)
        doc = Krita.instance().activeDocument()
        if name and doc is not None:
            commands.select_shape(doc, name)


def _button(text, handler, tooltip=None):
    button = QPushButton(text)
    if tooltip:
        button.setToolTip(tooltip)
    button.clicked.connect(lambda: handler())
    return button


def _combo(items, current):
    combo = QComboBox()
    combo.addItems(items)
    if current and combo.findText(current) < 0:
        # Keep a value the schema no longer knows, so the check can point at it.
        combo.addItem(current)
    if current:
        combo.setCurrentText(current)
    return combo
