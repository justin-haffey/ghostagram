/**
 * Browser rendering cadence isolated from mutation and validation. A renderer
 * owns its dirty state; this helper only guarantees one frame per turn.
 */
export class RenderScheduler {
  #frame = 0;

  constructor(
    flush,
    requestFrame = globalThis.requestAnimationFrame?.bind(globalThis) ?? (callback => setTimeout(callback, 0)),
    cancelFrame = globalThis.cancelAnimationFrame?.bind(globalThis) ?? clearTimeout
  ) {
    this.flush = flush;
    this.requestFrame = requestFrame;
    this.cancelFrame = cancelFrame;
  }

  get scheduled() { return this.#frame !== 0; }

  request() {
    if (!this.#frame) this.#frame = this.requestFrame(() => {
      this.#frame = 0;
      this.flush();
    });
  }

  cancel() {
    if (!this.#frame) return;
    this.cancelFrame(this.#frame);
    this.#frame = 0;
  }
}
