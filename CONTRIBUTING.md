# Contributing to CadSpace

Keep changes within the smallest reusable layer that owns the behavior. Geometry and document packages must not reference Uno, a browser, or a GPU. Controls must not reference the application project.

Run both executable regression suites before opening a pull request:

```sh
dotnet run --project tests/CadSpace.Tests -c Release
dotnet run --project tests/CadSpace.Exchange.Tests -c Release
```

For UI work, build both `net10.0-desktop` and `net10.0-browserwasm`. Exercise pointer selection, command-line coordinates, undo, layer locking, and file export. Include screenshots for visual changes and state the operating system, browser, renderer, and whether a physical GPU was used.

Every new command needs atomic-failure tests, undo/redo tests, coordinate and degeneracy tests, and a feature-coverage entry. Every DXF feature needs an independent fixture, read/write/read assertions, and tests protecting unrelated group data. Never silently drop unsupported geometry, metadata, or editing semantics.

Do not label triangle meshes as ACIS solids or claim native AutoCAD compatibility from self-roundtrip tests. Native AutoCAD open/AUDIT/save/reopen qualification is a separate gate. Do not add proprietary assets, commercial-only dependencies, or incompatible licenses.

Release numbers follow semantic versioning. Public API changes must be described in release notes. Tag `vX.Y.Z` to run the release workflow; packages are attached to the GitHub release, not automatically published to NuGet.org.
