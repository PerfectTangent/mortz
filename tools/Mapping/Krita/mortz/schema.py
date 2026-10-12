"""Game data from schema.json, written by `Mortz.Tools map schema`. Never hardcode it here."""

import json
import os

_PATH = os.path.join(os.path.dirname(__file__), "schema.json")


def load():
    with open(_PATH, encoding="utf-8") as file:
        return json.load(file)
