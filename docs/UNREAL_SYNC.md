# Unreal Engine 5 texture sync

Tessera exchanges PNG textures and Height, Normal and AO maps with the Unreal Editor. The bridge is editor only; it does not add a runtime dependency to packaged games. Unreal assets are imported under `/Game/TesseraSync`. Transfer files and state live under `<project>/Saved/TesseraSync`, so they are not game content.

## Connect

1. In Tessera, choose **File → Unreal Engine 5 → Choose project…** and select the project's `.uproject` file.
2. Choose **Install plugin into project…**. Tessera copies `TesseraSync` into the project's `Plugins` directory.
3. In Unreal, enable **Tessera Sync** and restart the editor. The plugin requires **Python Editor Script Plugin** and **Editor Scripting Utilities**. The plugin's descriptor requests both dependencies.

The connected project path is saved in Tessera's library settings. Use **Choose project…** again to switch projects. The copy step only replaces files inside `Plugins/TesseraSync`; it does not alter the `.uproject` file. Plugin updates require running **Install plugin into project…** again and restarting Unreal.

## Tessera → Unreal

Open an image, choose the source or a result as the processing basis, then choose **Send selected texture**. Tessera writes a PNG and a SHA-256 manifest atomically. The Unreal plugin checks for new manifests once per second and imports the texture under `/Game/TesseraSync` while the editor is open. A project can receive transfers queued while the editor is closed.

For maps, open the Height / Normal / AO editor for the selected image, then choose **Send material maps**. In a Tessera workspace, saved map sources can also be sent after closing the map editor. Newly imported map textures have sRGB disabled; Normal uses Unreal's normal-map compression. Unreal material graphs are not created or rewired automatically.

If a managed Unreal texture has changed since the previous import, the plugin skips the next incoming replacement and logs a conflict in Unreal's Output Log. Export that texture back to Tessera and choose which version to send next. An unrelated pre-existing asset with the same name is never replaced.

## Unreal → Tessera

In Unreal's Content Browser, select one or more `Texture2D` assets. Choose **Tools → Send selected textures to Tessera**. This writes PNGs with manifests under `<project>/Saved/TesseraSync/to_tessera`.

In Tessera, choose **File → Unreal Engine 5 → Receive textures from Unreal**. Each changed PNG is added as an independent source, including maps. Existing Tessera sources and editable `.tmaps` documents are preserved; reconcile a returned map in the map editor if needed. The same PNG revision is received only once. The library retains earlier received versions.

Only PNG pixels and the map kind cross the bridge. Unreal material graphs, texture groups, compression settings on existing assets, sprite metadata, animation and deletions do not synchronize. PNG export from a compressed Unreal texture can differ from Tessera's original pixels; review the result before using it as a new source.

## Verification

`dotnet run --project PixelArtAlignment.Tests -c Release --no-build -- --unreal-sync` checks the on-disk transfer protocol, including hashes, incomplete files and updated revisions. The UE 5.3 editor workflow must be checked in a project with the plugin enabled before treating import, export and the Tools menu as accepted.
