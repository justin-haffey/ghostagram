import { performance } from "node:perf_hooks";
import { __testing } from "../ghostagram.js";

const nodes = Array.from({ length: 1000 }, (_, index) => ({
  id: `node-${index}`, x: (index % 40) * 160, y: Math.floor(index / 40) * 96, width: 100, height: 48
}));
const ports = nodes.map(node => ({ id: `${node.id}-port`, nodeId: node.id, direction: "both" }));
const edges = Array.from({ length: 2000 }, (_, index) => ({
  id: `edge-${index}`,
  sourcePortId: `node-${index % 1000}-port`,
  targetPortId: `node-${(index * 17 + 23) % 1000}-port`,
  connector: index % 4 === 0 ? "bezier" : index % 4 === 1 ? "state-machine" : index % 4 === 2 ? "straight" : "flowchart"
}));
const state = __testing.buildState({ documentId: "benchmark", nodes, ports, edges });
const dirty = { all: false, nodes: new Set(), edges: new Set(), groups: new Set(), viewport: false, selection: false };
const started = performance.now();
__testing.applyOperation(state, { type: "node.upsert", value: { ...nodes[500], x: nodes[500].x + 16 } }, dirty, { minZoom: .2, maxZoom: 3 });
const elapsed = performance.now() - started;
const incidentEdges = [...dirty.edges].length;
const lassoStarted = performance.now();
const lassoIds = __testing.selectionIdsInRectangle(state, { x: 2400, y: 768, width: 960, height: 768 });
const lassoElapsed = performance.now() - lassoStarted;

console.log(JSON.stringify({ nodes: state.nodes.size, edges: state.edges.size, deltaMilliseconds: Number(elapsed.toFixed(3)), lassoMilliseconds: Number(lassoElapsed.toFixed(3)), lassoSelectionCount: lassoIds.length, dirtyNodes: dirty.nodes.size, dirtyEdges: incidentEdges }));
if (dirty.nodes.size !== 1 || incidentEdges === 0 || dirty.all || !lassoIds.length || elapsed > 100 || lassoElapsed > 100) process.exitCode = 1;
