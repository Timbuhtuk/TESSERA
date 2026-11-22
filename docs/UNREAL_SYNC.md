# Unreal Engine 5 projects in Tessera

Connect an Unreal project to browse the Content branches containing textures and edit PNG-compatible Texture2D assets, including Height, Normal and AO textures. Each texture folder appears with its ancestors. Saving a linked texture updates its existing Unreal asset. Unreal owns the folder structure.

## Install and connect

1. In the Tessera library, click **Connect Unreal project…** and select the project's `.uproject` file. The same action is available under **File → Unreal Engine 5**.
2. Accept the installation prompt. Tessera installs its bundled plugin into `<project>/Plugins/TesseraSync`. To reinstall it, select the project in the library and use **File → Unreal Engine 5 → Install plugin into project…**.
3. Open the project in Unreal Editor. Under **Edit → Plugins**, search for **Tessera Sync** and enable it. Accept the required dependencies **Python Editor Script Plugin** and **Editor Scripting Utilities**. Restart Unreal Editor after enabling or updating the plugin.
4. In Tessera, select the project with the Unreal Engine icon. When the plugin finishes indexing the project, the library shows the Content tree with its textures. Unreal Editor 5.3 is the integration target.

For Unreal assets, the right panel provides the preview, texture details, project location, and the action to open the texture. Right-click a linked texture to rename, move, or delete it in Unreal. Tags and local library management controls remain available for local Tessera assets.

If the project stays disconnected, check that the editor opened the same `.uproject`, the plugin is enabled, and Unreal was restarted. The Unreal Output Log shows plugin and import errors.

## Edit textures and maps

To create a new Unreal texture, select an image in the library and choose **File → Unreal Engine 5 → Create texture in Unreal…**. The image's context menu offers the same action. Tessera uses the processing version last selected in the image editor. Select the connected project, folder, and texture name. The dialog accepts a new folder path under `/Game`. A separate linked image is added to the project and queued for creation. New color textures use sRGB.

Creation works with Unreal closed. The pending texture stays in Tessera's tree across restarts. After Unreal confirms creation, subsequent **Ctrl+S** saves update that texture. An occupied name stops creation. Press **Ctrl+S** on the pending texture to choose a different name if a collision was discovered by Unreal.

Open a texture from the tree, process it, select the result as the editing basis, and press **Ctrl+S**. Tessera shows **Saved in Unreal** when the plugin confirms the write. Saving updates the existing texture object and retains its material references and import settings. Intermediate results remain in Tessera's history.

Open Unreal yourself to load textures for the first time. With the editor closed, you can browse the tree, edit previously opened textures, and queue saves. Tessera keeps the queue across restarts. If Unreal has unsaved changes in the same asset, save those changes in Unreal so Tessera can check the current revision. On a conflict, press **Ctrl+S** again and choose the Unreal version or explicitly replace it with the selected Tessera result.

If a texture moves, Tessera follows Unreal's redirector when one is available. If it loses the link, open the cached texture from **All assets** and use **Ctrl+S** to select a new destination. You can paste the Unreal Content Browser's **Copy Reference** value into the destination field.

In the Height / Normal / AO window for a linked source, use **Ctrl+S** to publish the maps. The first save asks where to put each map and suggests a sibling of the source texture. New maps use linear color; Normal uses Unreal's normal map compression. Connect the resulting textures to a material in Unreal as needed.

To rename or move an existing texture, right-click it in Tessera's library and choose **Rename or move in Unreal…**. Enter a new name or `/Game` folder. Tessera queues the operation while Unreal is closed. The plugin checks that the destination is free and that the original texture has not changed since the last catalog revision. Once Unreal confirms the move, the same Tessera asset and its result history follow the new path.

To delete a texture, right-click it and choose **Delete texture from Unreal…**. Unreal's editor API force-deletes the asset even if other assets reference it, so review the confirmation before proceeding. If the texture was opened in Tessera, its local image and result history stay under **All assets** after Unreal confirms deletion. The link is marked unavailable; **Ctrl+S** can bind that history to another destination later. An unopened texture has no local image to keep and disappears from Tessera's catalog. A queued move or delete can be retried after a conflict or other failure. If the texture changed in Unreal, Tessera asks before retrying against its current revision.

This build installs Tessera Sync 0.4.1. Reinstall the plugin from Tessera's Unreal menu and restart Unreal Editor after upgrading.

The editor plugin uses `<project>/Saved/TesseraSync` for its internal cache and queue. The integration has no dependency in the packaged game.

## Verification

`dotnet run --project Tessera.RegressionTests -c Release --no-build -- --unreal-sync` checks the installer, project tree and icon, save resumed after restart, and queued move/delete responses. `python tests/UnrealProjectChecks.py` exercises revision conflicts, occupied destinations, move, and delete with a stand-in for Unreal's asset API. Actual Editor behavior for move/delete and material references still needs an in-editor check.
