# Unreal Engine 5 projects in Tessera

Connect an Unreal project to browse the Content branches containing textures and edit PNG-compatible Texture2D assets, including Height, Normal and AO textures. Each texture folder appears with its ancestors. Saving a linked texture updates its existing Unreal asset. Unreal owns the folder structure.

## Install and connect

1. In the Tessera library, click **Connect Unreal project…** and select the project's `.uproject` file. The same action is available under **File → Unreal Engine 5**.
2. Accept the installation prompt. Tessera installs its bundled plugin into `<project>/Plugins/TesseraSync`. To reinstall it, select the project in the library and use **File → Unreal Engine 5 → Install plugin into project…**.
3. Open the project in Unreal Editor. Under **Edit → Plugins**, search for **Tessera Sync** and enable it. Accept the required dependencies **Python Editor Script Plugin** and **Editor Scripting Utilities**. Restart Unreal Editor after enabling or updating the plugin.
4. In Tessera, select the project with the Unreal Engine icon. When the plugin finishes indexing the project, the library shows the Content tree with its textures. Unreal Editor 5.3 is the verified engine version.

For Unreal assets, the right panel provides the preview, texture details, project location, and the action to open the texture. Tags and asset management controls are available for local Tessera assets.

If the project stays disconnected, check that the editor opened the same `.uproject`, the plugin is enabled, and Unreal was restarted. The Unreal Output Log shows plugin and import errors.

## Edit textures and maps

To create a new Unreal texture from an image or a selected Tessera result, use **File → Unreal Engine 5 → Create texture in Unreal…**. You can also choose this action from an image's context menu in the library. Select the connected project, folder, and texture name. The dialog accepts a new folder path under `/Game`. A separate linked image is added to the project and queued for creation. New color textures use sRGB.

Creation works with Unreal closed. The pending texture stays in Tessera's tree across restarts. After Unreal confirms creation, subsequent **Ctrl+S** saves update that texture. An occupied name stops creation. Press **Ctrl+S** on the pending texture to choose a different name if a collision was discovered by Unreal.

Open a texture from the tree, process it, select the result as the editing basis, and press **Ctrl+S**. Tessera shows **Saved in Unreal** when the plugin confirms the write. Saving updates the existing texture object and retains its material references and import settings. Intermediate results remain in Tessera's history.

Open Unreal yourself to load textures for the first time. With the editor closed, you can browse the tree, edit previously opened textures, and queue saves. Tessera keeps the queue across restarts. If Unreal has unsaved changes in the same asset, save those changes in Unreal so Tessera can check the current revision. On a conflict, press **Ctrl+S** again and choose the Unreal version or explicitly replace it with the selected Tessera result.

If a texture moves, Tessera follows Unreal's redirector when one is available. If it loses the link, open the cached texture from **All assets** and use **Ctrl+S** to select a new destination. You can paste the Unreal Content Browser's **Copy Reference** value into the destination field.

In the Height / Normal / AO window for a linked source, use **Ctrl+S** to publish the maps. The first save asks where to put each map and suggests a sibling of the source texture. New maps use linear color; Normal uses Unreal's normal map compression. Connect the resulting textures to a material in Unreal as needed.

The editor plugin uses `<project>/Saved/TesseraSync` for its internal cache and queue. The integration has no dependency in the packaged game.

## Legacy manual exchange from Tessera

Open an image, choose the source or a result as the processing basis, then choose **Send selected texture**. Tessera writes a PNG and a SHA-256 manifest atomically. The Unreal plugin checks for new manifests once per second and imports the texture under `/Game/TesseraSync` while the editor is open. A project can receive transfers queued while the editor is closed.

For maps, open the Height / Normal / AO editor for the selected image, then choose **Send material maps**. In a Tessera workspace, saved map sources can also be sent after closing the map editor. Newly imported map textures have sRGB disabled; Normal uses Unreal's normal-map compression. Unreal material graphs are not created or rewired automatically.

If a managed Unreal texture has changed since the previous import, the plugin skips the next incoming replacement and logs a conflict in Unreal's Output Log. Export that texture back to Tessera and choose which version to send next. An unrelated pre-existing asset with the same name is never replaced.

## Legacy manual exchange from Unreal

In Unreal's Content Browser, select one or more `Texture2D` assets. Choose **Tools → Send selected textures to Tessera**. This writes PNGs with manifests under `<project>/Saved/TesseraSync/to_tessera`.

In Tessera, choose **File → Unreal Engine 5 → Receive textures from Unreal**. Each changed PNG is added as an independent source, including maps. Existing Tessera sources and editable `.tmaps` documents are preserved; reconcile a returned map in the map editor if needed. The same PNG revision is received only once. The library retains earlier received versions.

Only PNG pixels and the map kind cross the manual bridge. PNG export from a compressed Unreal texture can differ from Tessera's original pixels.

## Verification

`dotnet run --project Tessera.RegressionTests -c Release --no-build -- --unreal-sync` checks the installer, manual transfer compatibility, project tree and icon, and a save resumed after Tessera restarts.
