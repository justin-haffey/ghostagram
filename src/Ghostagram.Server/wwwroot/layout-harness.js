import * as ghostagram from "./ghostagram/ghostagram.js";

const documentId = new URLSearchParams(window.location.search).get("documentId")?.trim() || "layout-harness";
const documentApiPath = `/api/documents/${encodeURIComponent(documentId)}`;
document.title = `Ghostagram · ${documentId}`;
const status = document.querySelector("#status");
let serverRevision = 0;
let renderRevision = 0;
let model;
let sequence = 0;
let synchronizing = false;

const hello = ghostagram.create(document.querySelector("#canvas"), {
  protocolVersion: 1,
  gridSize: 1,
  wheelZoom: true,
  eventSink: event => {
    if (event.type.endsWith(".commit")) {
      status.textContent = `Layout laboratory is read-only; use Preview or Commit · ${event.type}`;
    }
  }
});

const nodes = [
  node("ingest", "Ingest", 620, 420),
  node("validate", "Validate", 90, 520),
  node("enrich", "Enrich", 760, 60),
  node("review", "Human review", 260, 180, "quality-inner"),
  node("approve", "Approve", 900, 600, "quality"),
  node("publish", "Publish", 40, 40),
  node("retry", "Retry", 430, 640),
  node("audit-a", "Audit source", 1050, 100),
  node("audit-b", "Audit sink", 1050, 260)
];

const groups = [
  { id: "quality", label: "Quality stage", x: 180, y: 120, width: 420, height: 300 },
  { id: "quality-inner", label: "Review loop", parentGroupId: "quality", x: 220, y: 160, width: 220, height: 150 }
];

const edges = [
  edge("ingest", "validate"),
  edge("ingest", "enrich"),
  edge("validate", "review"),
  edge("enrich", "review"),
  edge("review", "approve"),
  edge("approve", "publish"),
  edge("approve", "retry"),
  edge("retry", "review"),
  edge("audit-a", "audit-b")
];

const ports = nodes.flatMap(item => [
  { id: `${item.id}-in`, nodeId: item.id, direction: "target", anchor: "left" },
  { id: `${item.id}-out`, nodeId: item.id, direction: "source", anchor: "right" }
]);

await initialize();
document.querySelector("#preview").addEventListener("click", () => layout(true));
document.querySelector("#commit").addEventListener("click", () => layout(false));
document.querySelector("#fit").addEventListener("click", fit);
window.setInterval(synchronizeRemoteChanges, 750);

async function initialize() {
  const existing = await fetch(documentApiPath);
  if (existing.ok) {
    const snapshot = await existing.json();
    serverRevision = snapshot.revision;
    model = snapshot.model;
  } else {
    const operations = [
      ...groups.map(value => ({ type: "group.upsert", value })),
      ...nodes.map(value => ({ type: "node.upsert", value })),
      ...ports.map(value => ({ type: "port.upsert", value })),
      ...edges.map(value => ({ type: "edge.upsert", value }))
    ];
    const result = await post(`${documentApiPath}/commands`, {
      documentId,
      actorId: "layout-lab",
      commandId: "initialize",
      baseRevision: 0,
      operations
    });
    serverRevision = result.revision;
    model = result.snapshot.model;
  }

  replaceModel("initial");
  status.textContent = `${documentId} · ready · server revision ${serverRevision} · use Preview for a non-persistent layout`;
}

async function synchronizeRemoteChanges() {
  if (synchronizing) return;
  synchronizing = true;
  try {
    const response = await fetch(documentApiPath);
    if (!response.ok) return;
    const snapshot = await response.json();
    if (snapshot.revision <= serverRevision) return;
    serverRevision = snapshot.revision;
    model = snapshot.model;
    replaceModel(`remote-${serverRevision}`);
    status.textContent = `Live session synchronized · authoritative revision ${serverRevision}`;
  } catch {
    // A transient poll failure does not disturb the current canvas.
  } finally {
    synchronizing = false;
  }
}

async function layout(dryRun) {
  setBusy(true);
  try {
    const request = {
      documentId,
      actorId: "layout-lab",
      commandId: dryRun ? `preview-${++sequence}` : `commit-${Date.now()}`,
      baseRevision: serverRevision,
      algorithm: "ghost-layered",
      seed: Number(document.querySelector("#seed").value),
      dryRun,
      options: {
        direction: document.querySelector("#direction").value,
        crossingSweeps: Number(document.querySelector("#sweeps").value)
      }
    };
    const result = await post(`${documentApiPath}/layout`, request);
    if (dryRun) {
      applyOperations(result.operations);
    } else {
      serverRevision = result.proposedRevision;
      model = result.commit.snapshot.model;
      replaceModel(`commit-${serverRevision}`);
    }

    const metrics = result.metrics;
    status.textContent = `${result.code} · ${result.algorithm} ${result.algorithmVersion} · ${metrics?.nodeCount ?? model.nodes.length} nodes · ${metrics?.componentCount ?? "replay"} components · crossings ${metrics?.estimatedCrossingsBefore ?? "–"} → ${metrics?.estimatedCrossingsAfter ?? "–"} · ${metrics?.elapsedMilliseconds?.toFixed(2) ?? "–"} ms · server revision ${serverRevision}`;
  } catch (error) {
    status.textContent = error.message;
  } finally {
    setBusy(false);
  }
}

function replaceModel(requestId) {
  const result = ghostagram.replace(hello.instanceId, {
    requestId,
    documentId,
    revision: ++renderRevision,
    model
  });
  if (!result.ok) throw new Error(`${result.problem.code}: ${result.problem.message}`);
  fit();
}

function applyOperations(operations) {
  for (const operation of operations) {
    const collection = operation.type.startsWith("node.") ? "nodes" : "groups";
    const index = model[collection].findIndex(item => item.id === operation.value.id);
    if (index >= 0) model[collection][index] = { ...model[collection][index], ...operation.value };
  }
  const result = ghostagram.apply(hello.instanceId, {
    requestId: `preview-render-${++sequence}`,
    documentId,
    baseRevision: renderRevision,
    revision: renderRevision + 1,
    ops: [...operations, { type: "viewport.fit", value: { padding: 72 } }]
  });
  if (!result.ok) throw new Error(`${result.problem.code}: ${result.problem.message}`);
  renderRevision += 1;
}

function fit() {
  const result = ghostagram.apply(hello.instanceId, {
    requestId: `fit-${++sequence}`,
    documentId,
    baseRevision: renderRevision,
    revision: renderRevision + 1,
    ops: [{ type: "viewport.fit", value: { padding: 72 } }]
  });
  if (result.ok) renderRevision += 1;
}

async function post(url, body) {
  const response = await fetch(url, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify(body)
  });
  const result = await response.json();
  if (!response.ok) {
    throw new Error(`${result.code ?? result.problem?.code ?? response.status}: ${result.message ?? result.problem?.message ?? "Request failed"}`);
  }
  return result;
}

function setBusy(value) {
  for (const button of document.querySelectorAll("button")) button.disabled = value;
}

function node(id, label, x, y, groupId) {
  return { id, label, x, y, width: 132, height: 62, ...(groupId ? { groupId } : {}) };
}

function edge(source, target) {
  return {
    id: `${source}-${target}`,
    sourcePortId: `${source}-out`,
    targetPortId: `${target}-in`,
    connector: "flowchart",
    connectorOptions: { stub: 34, cornerRadius: 8 },
    overlays: [{ type: "arrow" }]
  };
}
