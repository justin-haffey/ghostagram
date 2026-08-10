/**
 * Per-module protocol facade. It is intentionally the only layer that owns
 * instance handles; engine instances stay private implementation detail.
 */
export class ProtocolFacade {
  #instances = new Map();

  constructor(engineFactory, capabilities, missingInstance) {
    this.engineFactory = engineFactory;
    this.capabilities = capabilities;
    this.missingInstance = missingInstance;
  }

  create(host, options) {
    const engine = this.engineFactory(host, options);
    this.#instances.set(engine.instanceId, engine);
    return engine.hello();
  }

  hello(instanceId) { return this.#require(instanceId).hello(); }
  replace(instanceId, request) { return this.#require(instanceId).replace(request); }
  apply(instanceId, request) { return this.#require(instanceId).apply(request); }
  inspect(instanceId) { return this.#require(instanceId).inspect(); }
  clientToCanvas(instanceId, clientX, clientY) { return this.#require(instanceId).clientToCanvas(clientX, clientY); }
  canvasCenter(instanceId) { return this.#require(instanceId).canvasCenter(); }
  hitTestClientPoint(instanceId, clientX, clientY) { return this.#require(instanceId).hitTestClientPoint(clientX, clientY); }
  exportSvg(instanceId, options) { return this.#require(instanceId).exportSvg(options); }
  exportPng(instanceId, options) { return this.#require(instanceId).exportPng(options); }
  copyViewportPng(instanceId) { return this.#require(instanceId).copyViewportPng(); }

  dispose(instanceId) {
    const engine = this.#require(instanceId);
    const result = engine.dispose();
    this.#instances.delete(instanceId);
    return result;
  }

  #require(instanceId) {
    const engine = this.#instances.get(instanceId);
    if (!engine) throw this.missingInstance(instanceId);
    return engine;
  }
}
