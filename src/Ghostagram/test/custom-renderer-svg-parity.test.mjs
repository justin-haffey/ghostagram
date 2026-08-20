import { readFileSync } from "node:fs";
import test from "node:test";
import assert from "node:assert/strict";
import { __testing, registerNodeRenderer } from "../ghostagram.js";

const fixture = JSON.parse(readFileSync(
  new URL("../../../tests/fixtures/custom-renderer-svg-parity.v1.json", import.meta.url),
  "utf8"
));

test("browser SVG exporter satisfies the shared custom-renderer parity contract", () => {
  assert.equal(fixture.contractVersion, 1);
  let observedBounds;
  const unregister = registerNodeRenderer(fixture.renderer.key, fixture.renderer.version, {
    exportSvg({ node, bounds, escape }) {
      observedBounds = bounds;
      const value = node.properties.find(property => property.id === fixture.node.property.id)?.value;
      return `<rect x="${bounds.x + 8}" y="${bounds.y + 8}" width="${bounds.width - 16}" height="${bounds.height - 16}" rx="4" fill="#f0fdfa" stroke="#0f766e"/><text x="${bounds.x + 16}" y="${bounds.y + 30}" font-family="system-ui,sans-serif" font-size="12" fill="#134e4a" text-anchor="start">${escape(value)}</text>`;
    }
  });

  try {
    const registered = exportFixture(fixture.renderer.version);
    assert.deepEqual(observedBounds, fixture.expected.bodyBounds);
    assert.match(registered, new RegExp(`data-renderer-key="${fixture.renderer.key}" data-renderer-version="${fixture.renderer.version}"`));
    assert.equal(occurrences(registered, fixture.expected.customBodySvg), 1);
    assert.match(registered, new RegExp(fixture.expected.escapedValue.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")));
    assert.doesNotMatch(registered, /A&B <ready>/);

    const fallback = exportFixture(fixture.renderer.mismatchVersion);
    assert.equal(fixture.expected.fallbackMode, "standard");
    assert.doesNotMatch(fallback, /data-renderer-key=/);
    assert.equal(occurrences(fallback, fixture.expected.customBodySvg), 0);
    assert.match(fallback, />Value<\/text>/);
    assert.ok(fallback.includes(fixture.expected.escapedValue));
  } finally {
    unregister();
  }
});

function exportFixture(rendererVersion) {
  const property = fixture.node.property;
  const model = {
    documentId: "renderer-parity",
    nodes: [{
      id: fixture.node.id,
      label: fixture.node.label,
      x: fixture.node.x,
      y: fixture.node.y,
      width: fixture.node.width,
      height: fixture.node.height,
      rendererKey: fixture.renderer.key,
      rendererVersion,
      properties: [{ id: property.id, name: property.label, label: property.label, value: property.value }]
    }],
    ports: [],
    edges: [],
    groups: [],
    edgeTypes: []
  };
  return __testing.exportSvgDocument(__testing.buildState(model));
}

function occurrences(value, fragment) {
  return value.split(fragment).length - 1;
}
