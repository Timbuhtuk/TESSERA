# Writing patch notes

Patch notes describe changes that a person using Tessera can observe. Write the `next` entry in `catalog.json` in Russian and English before a release. Name the affected tool, state whether it was added, changed or removed, and explain what the user can now do or should expect to happen differently. Mention changed defaults, compatibility or limitations when they affect use. Do not invent user-visible changes for an internal maintenance task.

Use short, concrete bullets. For example:

- Background removal: choose the background color with the pipette and undo eraser strokes.
- Color replacement: clicking a palette color opens replacement with that color already selected.
- Library: saving a result now succeeds even when the library is inside deeply nested folders.

Do not include classes, methods, frameworks, storage/serialization mechanisms, implementation algorithms, build or deployment steps, test results, checksums, commit subjects, pull requests or internal refactoring. User-facing file formats, tool names, color modes and keyboard shortcuts are appropriate when they explain how to use an affected tool.

Technical explanations and validation belong in `docs/DEVELOPMENT.md` and pull requests. They never belong in a patch note. GitHub release descriptions and the in-app history share the same user-facing changes. Historical entries in this catalog replace unedited descriptions from older releases. A new release without edited patch notes fails preparation; commit logs and rebuild descriptions are never substituted automatically. Rerunning the same already-versioned release may reuse its prepared notes.
