from krita import DockWidgetFactory, DockWidgetFactoryBase, Extension, Krita

from . import actions
from .docker import MortzDocker

MENU = "tools/mortz"
DOCKER_ID = "mortz_map"


class MortzExtension(Extension):
    def setup(self):
        notifier = Krita.instance().notifier()
        notifier.setActive(True)
        notifier.imageSaved.connect(actions.on_image_saved)

    def createActions(self, window):
        for name, text, handler in actions.ALL:
            window.createAction(name, text, MENU).triggered.connect(handler)


def register():
    app = Krita.instance()
    app.addExtension(MortzExtension(app))
    app.addDockWidgetFactory(DockWidgetFactory(
        DOCKER_ID, DockWidgetFactoryBase.DockPosition.DockRight, MortzDocker))
