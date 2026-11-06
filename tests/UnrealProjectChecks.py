"""Mailbox mutation checks with a small stand-in for Unreal Editor's asset API."""

import hashlib
import importlib.util
import os
import pathlib
import sys
import tempfile
import types
import unittest
from unittest.mock import patch


SCRIPT = pathlib.Path(__file__).resolve().parents[1] / "Unreal/TesseraSync/Content/Python/tessera_project.py"


class UnrealProjectChecks(unittest.TestCase):
    def setUp(self):
        scratch = SCRIPT.parents[4] / "artifacts" / "unreal-plugin-checks"
        scratch.mkdir(parents=True, exist_ok=True)
        self.temporary = tempfile.TemporaryDirectory(dir=scratch)
        self.addCleanup(self.temporary.cleanup)
        self.root = pathlib.Path(self.temporary.name)
        self.content = self.root / "Content"
        self.content.mkdir()
        self.job = self.root / "job"
        self.job.mkdir()
        self.dirty = False

        class Texture2D:
            def __init__(self, path):
                self.path = path

            def get_path_name(self):
                return self.path

        self.Texture2D = Texture2D

        def filename(package):
            return self.content / (package.removeprefix("/Game/") + ".uasset")

        def load(package):
            return Texture2D(package + "." + package.rsplit("/", 1)[-1]) if filename(package).exists() else None

        def rename(source, target):
            if not filename(source).exists() or filename(target).exists():
                return False
            filename(target).parent.mkdir(parents=True, exist_ok=True)
            os.replace(filename(source), filename(target))
            return True

        def delete(package):
            if not filename(package).exists():
                return False
            filename(package).unlink()
            return True

        self.filename = filename
        editor = types.SimpleNamespace(
            does_asset_exist=lambda package: filename(package).exists(),
            load_asset=load,
            rename_asset=rename,
            delete_asset=delete,
            save_loaded_asset=lambda texture, only_if_is_dirty=False: True,
        )
        unreal = types.SimpleNamespace(
            Paths=types.SimpleNamespace(project_content_dir=lambda: str(self.content), convert_relative_path_to_full=lambda p: p),
            AssetRegistryHelpers=types.SimpleNamespace(get_asset_registry=lambda: types.SimpleNamespace(is_loading_assets=lambda: False)),
            EditorAssetLibrary=editor,
            EditorLoadingAndSavingUtils=types.SimpleNamespace(get_dirty_content_packages=lambda: [types.SimpleNamespace(get_path_name=lambda: "/Game/A/Stone")] if self.dirty else []),
            Texture2D=Texture2D,
        )
        legacy = types.SimpleNamespace(
            ROOT=str(self.root / "Saved"),
            _hash=lambda path: hashlib.sha256(pathlib.Path(path).read_bytes()).hexdigest(),
            _export_texture=lambda texture, path: pathlib.Path(path).write_bytes(b"exported") or True,
        )
        with patch.dict(sys.modules, {"unreal": unreal, "tessera_sync": legacy}):
            spec = importlib.util.spec_from_file_location("tessera_project_check", SCRIPT)
            self.plugin = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(self.plugin)
        filename("/Game/A/Stone").parent.mkdir(parents=True)
        filename("/Game/A/Stone").write_bytes(b"source")
        self.source = "/Game/A/Stone.Stone"

    def request(self, operation, revision=None, destination=None):
        return {"operation": operation, "path": self.source,
                "revision": revision or self.plugin._revision("/Game/A/Stone"), "destination": destination}

    def test_move_keeps_identity_and_revision(self):
        target = "/Game/B/StoneMoved.StoneMoved"
        result = self.plugin._process(str(self.job), self.request("move", destination=target))
        self.assertEqual(result["status"], "moved")
        self.assertFalse(self.filename("/Game/A/Stone").exists())
        self.assertTrue(self.filename("/Game/B/StoneMoved").exists())
        self.assertEqual(result["revision"], self.plugin._revision("/Game/B/StoneMoved"))
        self.assertFalse((self.job / "unreal.png").exists())

    def test_conflict_and_dirty_package_do_not_delete(self):
        self.dirty = True
        self.assertIsNone(self.plugin._process(str(self.job), self.request("delete")))
        self.dirty = False
        result = self.plugin._process(str(self.job), self.request("delete", revision="old"))
        self.assertEqual(result["status"], "conflict")
        self.assertFalse((self.job / "unreal.png").exists())
        self.assertTrue(self.filename("/Game/A/Stone").exists())
        result = self.plugin._process(str(self.job), self.request("delete"))
        self.assertEqual(result["status"], "deleted")
        self.assertFalse(self.filename("/Game/A/Stone").exists())

    def test_move_rejects_occupied_destination(self):
        target = "/Game/B/Occupied.Occupied"
        self.filename("/Game/B/Occupied").parent.mkdir(parents=True)
        self.filename("/Game/B/Occupied").write_bytes(b"other")
        result = self.plugin._process(str(self.job), self.request("move", destination=target))
        self.assertEqual(result["status"], "occupied")
        self.assertTrue(self.filename("/Game/A/Stone").exists())


if __name__ == "__main__":
    unittest.main()
