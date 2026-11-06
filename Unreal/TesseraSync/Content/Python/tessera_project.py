"""Project catalog and request mailbox. The legacy bridge has its own queue."""

import os
import re
import shutil
import tempfile
import time

import unreal
import tessera_sync as legacy


ROOT = os.path.join(legacy.ROOT, "v2")
REQUESTS = os.path.join(ROOT, "requests")
CONTENT = os.path.realpath(unreal.Paths.convert_relative_path_to_full(unreal.Paths.project_content_dir()))
SYSTEM_FOLDERS = {"__externalactors__", "__externalobjects__"}
REQUEST_ID = re.compile(r"^[a-f0-9]{32}$")
_last_catalog = None
_last_scan = 0
_last_heartbeat = 0
_redirect_cache = {}


def _visible(path):
    return (path == "/Game" or path.startswith("/Game/")) and not any(
        part.lower() in SYSTEM_FOLDERS for part in path.split("/")
    )


def _package(path):
    package, separator, name = path.partition(".")
    parts = package.split("/")[2:]
    if not separator or not _visible(package) or not parts or name != parts[-1]:
        raise ValueError("Invalid Unreal object path")
    if any(not part or any(c in part for c in '\\.:*?"<>|\r\n') for part in parts):
        raise ValueError("Invalid Unreal package name")
    return package


def _revision(package):
    filename = os.path.join(CONTENT, package[len("/Game/"):] + ".uasset")
    try:
        stat = os.stat(filename)
        return f"{stat.st_mtime_ns}:{stat.st_size}"
    except FileNotFoundError:
        return ""


def publish_catalog():
    global _last_catalog
    registry = unreal.AssetRegistryHelpers.get_asset_registry()
    if registry.is_loading_assets():
        return
    folders = set()
    textures, redirects = [], {}
    for data in registry.get_assets_by_path("/Game", recursive=True, include_only_on_disk_assets=True) or []:
        folder = str(data.package_path)
        if not _visible(folder):
            continue
        package = str(data.package_name)
        path = package + "." + str(data.asset_name)
        kind = str(data.asset_class_path.asset_name)
        if kind not in ("Texture2D", "ObjectRedirector"):
            continue
        revision = _revision(package)
        if not revision:
            continue
        if kind == "Texture2D":
            textures.append({"path": path, "revision": revision})
            while _visible(folder):
                folders.add(folder)
                folder = folder.rsplit("/", 1)[0]
        else:
            cached = _redirect_cache.get(path)
            if cached is None or cached[0] != revision:
                tag = data.get_tag_value("DestinationObject") or ""
                # The registry stores a class-qualified object path for redirectors.
                target = tag[tag.find("/"):].rstrip("'") if "/" in tag else ""
                if not target:
                    texture = unreal.load_asset(path)
                    target = texture.get_path_name() if isinstance(texture, unreal.Texture2D) else ""
                cached = _redirect_cache[path] = (revision, target)
            if cached[1] and cached[1] != path:
                redirects[path] = cached[1]
    texture_paths = {item["path"] for item in textures}
    for path, target in list(redirects.items()):
        visited = {path}
        while target in redirects and target not in visited:
            visited.add(target)
            target = redirects[target]
        if target in texture_paths:
            redirects[path] = target
        else:
            redirects.pop(path, None)
    catalog = {"version": 2, "folders": sorted(folders), "textures": sorted(textures, key=lambda item: item["path"]),
               "redirects": redirects}
    if catalog != _last_catalog or not os.path.isfile(os.path.join(ROOT, "catalog.json")):
        legacy._write_json(os.path.join(ROOT, "catalog.json"), catalog)
        _last_catalog = catalog


def _export(texture, directory):
    final = os.path.join(directory, "unreal.png")
    temporary = os.path.join(directory, "export.png")
    try:
        if not legacy._export_texture(texture, temporary):
            raise ValueError("Unreal cannot export this texture as PNG")
        digest = legacy._hash(temporary)
        os.replace(temporary, final)
        return digest
    finally:
        if os.path.isfile(temporary):
            os.remove(temporary)


def _import(package, payload, existing, kind):
    folder, name = package.rsplit("/", 1)
    old_import = existing.get_editor_property("asset_import_data") if existing else None
    old_files = old_import.extract_filenames() if old_import else []
    # Use the actual asset name as the source filename for both legacy and Interchange importers.
    with tempfile.TemporaryDirectory(prefix="import-", dir=ROOT) as staging:
        filename = os.path.join(staging, name + ".png")
        shutil.copyfile(payload, filename)
        task = unreal.AssetImportTask()
        task.filename = filename
        task.destination_path = folder
        task.destination_name = name
        task.automated = True
        task.replace_existing = existing is not None
        task.replace_existing_settings = False
        task.save = False
        # UE 5.3's texture factory supports in-place replacement and preserves user settings.
        task.factory = unreal.TextureFactory()
        unreal.AssetToolsHelpers.get_asset_tools().import_asset_tasks([task])
        results = task.get_editor_property("imported_object_paths")
        path = package + "." + name
        if path not in results:
            raise ValueError("Unreal did not import the requested asset")
        texture = unreal.EditorAssetLibrary.load_asset(package)
        if not isinstance(texture, unreal.Texture2D) or (existing and texture != existing):
            raise ValueError("Unreal did not update the existing Texture2D in place")
        if not existing:
            texture.set_editor_property("srgb", kind == "color")
            if kind == "normal":
                texture.set_editor_property("compression_settings", unreal.TextureCompressionSettings.TC_NORMALMAP)
        import_data = texture.get_editor_property("asset_import_data")
        if import_data:
            if existing and old_files:
                for index, source in enumerate(old_files):
                    import_data.scripted_add_filename(source, index, "")
            elif not existing:
                # A new texture can be reimported without relying on a temporary staging folder.
                source_dir = os.path.join(ROOT, "sources", *package.split("/")[2:-1])
                os.makedirs(source_dir, exist_ok=True)
                source = os.path.join(source_dir, name + ".png")
                shutil.copyfile(payload, source)
                import_data.scripted_add_filename(source, 0, "")
        if not unreal.EditorAssetLibrary.save_loaded_asset(texture, only_if_is_dirty=False):
            raise ValueError("Unreal could not save the texture package")
    return texture


def _process(directory, request):
    if unreal.AssetRegistryHelpers.get_asset_registry().is_loading_assets():
        return None
    path = request["path"]
    package = _package(path)
    operation = request["operation"]
    if operation not in ("read", "save", "create", "move", "delete"):
        raise ValueError("Unknown Tessera operation")
    exists = unreal.EditorAssetLibrary.does_asset_exist(package)
    texture = unreal.EditorAssetLibrary.load_asset(package) if exists else None
    if operation == "create":
        if exists or _revision(package):
            return {"status": "occupied"}
        if request.get("kind") not in ("color", "height", "normal", "ao"):
            raise ValueError("Unsupported texture kind")
    elif not isinstance(texture, unreal.Texture2D) or texture.get_path_name() != path:
        return {"status": "missing"}
    else:
        dirty = unreal.EditorLoadingAndSavingUtils.get_dirty_content_packages()
        if any(item.get_path_name() == package for item in dirty):
            return None  # Wait for the user's changes in Unreal to be saved.
        revision = _revision(package)
        if not revision:
            return {"status": "missing"}
        if operation == "read" or revision != request.get("revision"):
            if operation in ("move", "delete"):
                return {"status": "conflict", "revision": revision}
            digest = _export(texture, directory)
            return {"status": "read" if operation == "read" else "conflict", "revision": revision, "sha256": digest}
    if operation == "move":
        destination = request.get("destination")
        if not isinstance(destination, str) or destination == path:
            raise ValueError("Invalid destination for Unreal texture")
        target = _package(destination)
        if unreal.EditorAssetLibrary.does_asset_exist(target) or _revision(target):
            return {"status": "occupied"}
        if not unreal.EditorAssetLibrary.rename_asset(package, target):
            raise ValueError("Unreal could not move the texture")
        moved = unreal.EditorAssetLibrary.load_asset(target)
        if not isinstance(moved, unreal.Texture2D) or moved.get_path_name() != destination:
            raise ValueError("Unreal did not move the requested Texture2D")
        if not unreal.EditorAssetLibrary.save_loaded_asset(moved, only_if_is_dirty=False):
            raise ValueError("Unreal could not save the moved texture")
        revision = _revision(target)
        if not revision:
            raise ValueError("Unreal did not save the moved texture")
        return {"status": "moved", "revision": revision}
    if operation == "delete":
        if not unreal.EditorAssetLibrary.delete_asset(package):
            raise ValueError("Unreal could not delete the texture")
        return {"status": "deleted"}
    payload = os.path.join(directory, "local.png")
    if legacy._hash(payload) != request.get("sha256"):
        raise ValueError("Tessera PNG checksum mismatch")
    texture = _import(package, payload, texture, request.get("kind"))
    digest = _export(texture, directory)
    return {"status": "saved", "revision": _revision(package), "sha256": digest}


def process_requests(limit=1):
    if not os.path.isdir(REQUESTS):
        return
    completed = 0
    for name in sorted(os.listdir(REQUESTS), key=lambda item: os.path.getmtime(os.path.join(REQUESTS, item))
                       if os.path.exists(os.path.join(REQUESTS, item)) else 0):
        if not REQUEST_ID.fullmatch(name):
            continue
        directory = os.path.join(REQUESTS, name)
        response = os.path.join(directory, "response.json")
        if os.path.isfile(response):
            continue
        request = legacy._read_json(os.path.join(directory, "request.json"), None)
        if not isinstance(request, dict) or request.get("version") != 2:
            continue
        try:
            result = _process(directory, request)
        except Exception as error:
            result = {"status": "error", "message": str(error)}
            unreal.log_warning("Tessera project: " + str(error))
        if result is not None:
            if result.get("status") in ("saved", "moved", "deleted"):
                publish_catalog()
            legacy._write_json(response, result)
            completed += 1
        if completed >= limit:
            break


def tick():
    global _last_scan, _last_heartbeat
    now = time.monotonic()
    if now - _last_heartbeat >= 2:
        legacy._write_json(os.path.join(ROOT, "editor.json"), {"version": 2})
        _last_heartbeat = now
    process_requests()
    if now - _last_scan >= 5:
        publish_catalog()
        _last_scan = now
