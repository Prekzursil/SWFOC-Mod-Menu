# External / Upstream Tools (Not Vendored)

Some third-party libraries this project builds on are **deliberately not vendored**
into this repository. This page records what they are and where to obtain them.

> This is distinct from `docs/EXTERNAL_TOOLS_SETUP.md`, which covers CI
> quality-signal integrations (SonarCloud, Applitools, releases). This page is
> about upstream source libraries the editor/bridge depends on.

## PetroglyphTools

**PetroglyphTools** is the open-source Petroglyph / Star Wars: Empire at War —
Forces of Corruption (PG/SWFOC) **game file-format library**. It provides
readers/writers for the formats this project's editor and bridge parse:

- **DAT** (localisation / string tables)
- **MEG** (mega-file archives)
- **MTD** (texture/mouse-texture descriptors)
- **Xml** (PG game XML)
- **AnimationSfxMap** (animation SFX trigger maps)
- **Localisation** helpers

### Why it is not vendored here

PetroglyphTools multi-targets **.NET 10**. GitHub's native
**Automatic dependency submission** (dependency-graph) feature restores every
`*.csproj` it finds using the runner's installed .NET SDK; that SDK cannot
restore `net10` projects, so vendoring PetroglyphTools turned the
"Automatic dependency submission" check **red** on every push. Keeping the
library out of this repo keeps that check green and keeps this repo on its
pinned .NET 8 SDK (`global.json`).

The reverse-engineering notes that reference PetroglyphTools remain in this repo
under `knowledge-base/` (those are notes/data, not the library).

### Where to get it

Clone or build PetroglyphTools from its open-source upstream:

- **Upstream:** <https://github.com/AlamoEngine-Tools/PetroglyphTools>
  (the **AlamoEngine-Tools** organization)

Obtain it **separately** from this repository — either:

1. clone and build the upstream solution yourself, or
2. reference its published packages (NuGet) from upstream.

The editor/bridge code in this repo uses PetroglyphTools to parse PG game files;
point your local build at your own checkout or package reference of the upstream
library rather than expecting it inside this tree.
