# Krita 6 ships PyQt6, Krita 5 PyQt5. Everything else imports Qt from here.
try:
    from PyQt6.QtCore import (QBuffer, QByteArray, QCoreApplication, QIODevice, QPoint, QPointF,
                              QSignalBlocker, Qt, QTimer)
    from PyQt6.QtGui import QBrush, QColor, QImage
    from PyQt6.QtWidgets import (QAbstractItemView, QCheckBox, QComboBox, QDoubleSpinBox,
                                 QFileDialog, QFormLayout, QGridLayout, QGroupBox, QHBoxLayout,
                                 QHeaderView, QLabel, QLineEdit, QListWidget, QListWidgetItem,
                                 QMessageBox, QPushButton, QScrollArea, QSpinBox,
                                 QStackedWidget, QTableWidget, QToolButton, QVBoxLayout, QWidget)
except ImportError:
    from PyQt5.QtCore import (QBuffer, QByteArray, QCoreApplication, QIODevice, QPoint, QPointF,
                              QSignalBlocker, Qt, QTimer)
    from PyQt5.QtGui import QBrush, QColor, QImage
    from PyQt5.QtWidgets import (QAbstractItemView, QCheckBox, QComboBox, QDoubleSpinBox,
                                 QFileDialog, QFormLayout, QGridLayout, QGroupBox, QHBoxLayout,
                                 QHeaderView, QLabel, QLineEdit, QListWidget, QListWidgetItem,
                                 QMessageBox, QPushButton, QScrollArea, QSpinBox,
                                 QStackedWidget, QTableWidget, QToolButton, QVBoxLayout, QWidget)
