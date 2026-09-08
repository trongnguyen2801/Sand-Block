# Collector block authoring

Integrated into Tools > #16 Sand Flow Puzzle > Level Editor, below the pictures.
Enable Use Authored Layout to edit the lower board. Modes follow the BlockX editor:
Edit Grid, View, Edit (drag), and Add (ordered cell painting, first cell is anchor).
Invalid moves, overlapping cells, disabled cells, and shrinking through blocks are rejected.

The interaction, draft, viewport, state, transactional document, clone and coordinate
logic was ported from `g58-marble-sort-color-block/Assets/GameS/BlockX/Scripts/CoreGameplay`.
The validator applies the same BlockX occupancy rules directly to its DTO, without
bringing the other project's gameplay controllers into this package.

Adaptations:
- Documents are embedded in LevelData.collectorBoard. Valid edits update the in-memory
  level; Save All persists both pictures and blocks using the existing level workflow.
- The renderer uses the sand palette. collectorPalette preserves RGB bindings when
  picture palette IDs change; missing colors are reported before save/play.
- Import BlockX Layout JSON reads version-1 layout data. Imported color IDs refer to
  the current combined picture palette (1-based); fix any mismatches before saving.
- Gameplay spawns authored shapes at exact board cells and splits each sand color's
  quota across its blocks. Disabled cells get collision barriers and cannot accept drops.
- Levels with authored layout disabled keep the existing automatic gameplay layout.

Data/interaction checks: Tests/BlockAuthoring at the repository root.
