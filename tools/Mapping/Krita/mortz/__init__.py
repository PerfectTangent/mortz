try:
    import krita
except ImportError:
    krita = None

if krita is not None:
    from . import extension

    extension.register()
