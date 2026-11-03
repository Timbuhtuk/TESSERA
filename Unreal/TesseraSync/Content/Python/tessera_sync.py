"""Editor-only PNG bridge. Only /Game/TesseraSync is modified by incoming transfers."""

import hashlib
import json
import os
import re
import tempfile
import time

import unreal


ROOT = os.path.join(unreal.Paths.project_saved_dir(), "TesseraSync")
INCOMING = os.path.join(ROOT, "to_unreal")
OUTGOING = os.path.join(ROOT, "to_tessera")
STATE_PATH = os.path.join(ROOT, "unreal-state.json")
DESTINATION = "/Game/TesseraSync"
NAME_PATTERN = re.compile(r"^[A-Za-z][A-Za-z0-9_]{0,63}$")
_tick_handle = None
_last_poll = 0.0
_reported_conflicts = set()


def _conflict(asset_path, revision, message):
    key = (asset_path, revision)
    if key not in _reported_conflicts:
        _reported_conflicts.add(key)
        unreal.log_warning("Tessera Sync: " + message + " " + asset_path)


def _hash(path):
    digest = hashlib.sha256()
    with open(path, "rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest().upper()


def _read_json(path, default):
    try:
        with open(path, "r", encoding="utf-8") as stream:
            return json.load(stream)
    except (OSError, ValueError):
        return default


def _write_json(path, value):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    handle, temporary = tempfile.mkstemp(prefix=".tessera-", suffix=".tmp", dir=os.path.dirname(path))
    try:
        with os.fdopen(handle, "w", encoding="utf-8") as stream:
            json.dump(value, stream, ensure_ascii=False, indent=2)
        os.replace(temporary, path)
    finally:
        if os.path.exists(temporary):
            os.remove(temporary)


def _export_texture(texture, path):
    task = unreal.AssetExportTask()
    task.set_editor_property("object", texture)
    task.set_editor_property("filename", path)
    task.set_editor_property("exporter", unreal.TextureExporterPNG())
    task.set_editor_property("automated", True)
    task.set_editor_property("prompt", False)
    task.set_editor_property("replace_identical", True)
    return bool(unreal.Exporter.run_asset_export_task(task)) and os.path.isfile(path)


def _texture_hash(texture):
    os.makedirs(ROOT, exist_ok=True)
    handle, path = tempfile.mkstemp(prefix=".compare-", suffix=".png", dir=ROOT)
    os.close(handle)
    try:
        os.remove(path)
        return _hash(path) if _export_texture(texture, path) else None
    finally:
        if os.path.exists(path):
            os.remove(path)


def _transfer_files(directory, transfer):
    name = transfer.get("AssetName", "")
    if not isinstance(name, str) or not NAME_PATTERN.fullmatch(name):
        return None
    path = os.path.join(directory, name + ".png")
    expected = transfer.get("Sha256", "")
    if not isinstance(expected, str) or not re.fullmatch(r"[A-Fa-f0-9]{64}", expected):
        return None
    try:
        if _hash(path) != expected.upper():
            return None
    except OSError:
        return None
    return path


def _import_one(transfer, png, state):
    name = transfer["AssetName"]
    kind = transfer.get("Kind", "color")
    if kind not in ("color", "height", "normal", "ao"):
        return False
    asset_path = DESTINATION + "/" + name
    incoming_hash = transfer["Sha256"].upper()
    revision = transfer.get("Revision") or incoming_hash
    previous = state.get(asset_path)
    if previous and previous.get("revision") == revision:
        return False
    existing = unreal.EditorAssetLibrary.load_asset(asset_path) if unreal.EditorAssetLibrary.does_asset_exist(asset_path) else None
    if existing:
        if not isinstance(existing, unreal.Texture2D) or not previous:
            _conflict(asset_path, revision, "asset collision at")
            return False
        current_hash = _texture_hash(existing)
        if current_hash is None or current_hash != previous.get("baseline"):
            _conflict(asset_path, revision, "Unreal texture changed; export it to Tessera before replacing")
            return False

    task = unreal.AssetImportTask()
    task.set_editor_property("filename", png)
    task.set_editor_property("destination_path", DESTINATION)
    task.set_editor_property("destination_name", name)
    task.set_editor_property("automated", True)
    task.set_editor_property("replace_existing", True)
    task.set_editor_property("replace_existing_settings", False)
    task.set_editor_property("save", False)
    unreal.AssetToolsHelpers.get_asset_tools().import_asset_tasks([task])
    texture = unreal.EditorAssetLibrary.load_asset(asset_path)
    if not isinstance(texture, unreal.Texture2D):
        unreal.log_warning("Tessera Sync: import failed for " + asset_path)
        return False
    if kind != "color" and not existing:
        texture.set_editor_property("srgb", False)
        if kind == "normal":
            texture.set_editor_property("compression_settings", unreal.TextureCompressionSettings.TC_NORMALMAP)
    if not unreal.EditorAssetLibrary.save_loaded_asset(texture, only_if_is_dirty=False):
        unreal.log_warning("Tessera Sync: could not save " + asset_path)
        return False
    baseline = _texture_hash(texture)
    if baseline is None:
        unreal.log_warning("Tessera Sync: could not verify " + asset_path)
        return False
    state[asset_path] = {"input": incoming_hash, "revision": revision, "baseline": baseline, "id": transfer.get("Id", ""), "kind": kind}
    unreal.log("Tessera Sync: imported " + asset_path)
    return True


def sync_from_tessera():
    if not os.path.isdir(INCOMING):
        return 0
    state = _read_json(STATE_PATH, {})
    if not isinstance(state, dict):
        state = {}
    imported = 0
    for filename in sorted(os.listdir(INCOMING)):
        if not filename.endswith(".json"):
            continue
        transfer = _read_json(os.path.join(INCOMING, filename), {})
        if not isinstance(transfer, dict) or transfer.get("AssetName") != filename[:-5]:
            continue
        png = _transfer_files(INCOMING, transfer)
        if png is None:
            continue
        try:
            if _import_one(transfer, png, state):
                imported += 1
        except Exception as error:
            unreal.log_warning("Tessera Sync: " + str(error))
    if imported:
        _write_json(STATE_PATH, state)
    return imported


def send_selected_to_tessera():
    os.makedirs(OUTGOING, exist_ok=True)
    state = _read_json(STATE_PATH, {})
    if not isinstance(state, dict):
        state = {}
    sent = 0
    for texture in unreal.EditorUtilityLibrary.get_selected_assets():
        if not isinstance(texture, unreal.Texture2D):
            continue
        asset_path = texture.get_path_name().split(".")[0]
        owned = state.get(asset_path, {})
        name = texture.get_name()
        if not NAME_PATTERN.fullmatch(name):
            name = "T_Texture_" + hashlib.sha256(asset_path.encode("utf-8")).hexdigest()[:12]
        elif not owned:
            name = name[:45] + "_" + hashlib.sha256(asset_path.encode("utf-8")).hexdigest()[:10]
        output = os.path.join(OUTGOING, name + ".png")
        handle, temporary = tempfile.mkstemp(prefix=".tessera-", suffix=".png", dir=OUTGOING)
        os.close(handle)
        try:
            os.remove(temporary)
            if not _export_texture(texture, temporary):
                unreal.log_warning("Tessera Sync: PNG export failed for " + asset_path)
                continue
            digest = _hash(temporary)
            os.replace(temporary, output)
            _write_json(os.path.join(OUTGOING, name + ".json"), {
                "Id": owned.get("id", "unreal:" + asset_path),
                "AssetName": name,
                "Kind": owned.get("kind", "color"),
                "Sha256": digest,
            })
            if owned:
                if owned.get("baseline") != digest:
                    pending = _read_json(os.path.join(INCOMING, texture.get_name() + ".json"), {})
                    if isinstance(pending, dict) and pending.get("AssetName") == texture.get_name():
                        revision = pending.get("Revision") or pending.get("Sha256")
                        if isinstance(revision, str):
                            owned["revision"] = revision
                owned["baseline"] = digest
            sent += 1
        finally:
            if os.path.exists(temporary):
                os.remove(temporary)
    _write_json(STATE_PATH, state)
    unreal.log("Tessera Sync: exported {} selected textures".format(sent))
    return sent


def _tick(delta_seconds):
    global _last_poll
    now = time.monotonic()
    if now - _last_poll < 1.0:
        return
    _last_poll = now
    sync_from_tessera()
    try:
        import tessera_project
        tessera_project.tick()
    except Exception as error:
        unreal.log_warning("Tessera project: " + str(error))


def start():
    global _tick_handle
    if _tick_handle is not None:
        return
    menu = unreal.ToolMenus.get().extend_menu("LevelEditor.MainMenu.Tools")
    menu.add_section("TesseraSync", "Tessera Sync")
    entry = unreal.ToolMenuEntry(name="TesseraSync.ExportSelected", type=unreal.MultiBlockType.MENU_ENTRY)
    entry.set_label("Send selected textures to Tessera")
    entry.set_tool_tip("Export selected Texture2D assets as PNG for Tessera")
    entry.set_string_command(unreal.ToolMenuStringCommandType.PYTHON, "", "import tessera_sync; tessera_sync.send_selected_to_tessera()")
    menu.add_menu_entry("TesseraSync", entry)
    unreal.ToolMenus.get().refresh_all_widgets()
    _tick_handle = unreal.register_slate_post_tick_callback(_tick)
    unreal.log("Tessera Sync: watching " + INCOMING)
