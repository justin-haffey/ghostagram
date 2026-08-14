# UI and runtime rendering

## Node presentation

The existing node container remains the drag, selection, resize, rotation, grouping, and label-edit surface. Dynamic properties render inside that container as deterministic rows. This avoids changing the proven group and pointer interaction system.

Display-mode properties render a label and formatted value. Edit-mode primitive properties render an appropriate accessible control and emit `node.property.commit`; the host validates and commits a normal authoritative node update before the browser receives the resulting delta.

## Property ports

Ports linked to a property appear beside that row: inputs on the left, outputs on the right. Their model anchor is derived from property order and the same row geometry used by the DOM. Edge routing therefore remains deterministic during drag, export, and headless tests.

Legacy ports continue to use their configured side, relative, automatic, or perimeter anchors.

## Icon authoring

The node designer offers curated presets for fast selection plus an advanced Iconify-name field. Definitions provide a default icon and instances may override it. Icons remain presentational; execution never branches on them.

## Connectors

The laboratory exposes straight, tangent-preserving Bezier, arched Curved, and square/flowchart connectors. Arrow overlays are independent of connector geometry and remain visible for all four shapes.

## Reusable boundaries

`GhostPalette` remains a dependency-free Blazor control. The host projects DI catalog groups/items into it and supplies icon rendering through its existing render fragment. Laboratory persistence, node designer state, and execution registration do not move into the palette.

