/**
 * Small, framework-free command dispatcher used by the graph reducer.
 *
 * It deliberately knows nothing about DOM, state shape, or transport. The
 * reducer supplies named handlers; this keeps protocol operation routing
 * inspectable and makes a future C# command translator map to the same list.
 */
export class OperationDispatcher {
  #handlers;

  constructor(handlers, unsupported) {
    this.#handlers = new Map(Object.entries(handlers));
    this.unsupported = unsupported;
  }

  get types() { return [...this.#handlers.keys()]; }

  dispatch(operation, context) {
    const handler = this.#handlers.get(operation.type);
    if (!handler) return this.unsupported(operation);
    return handler(context, operation);
  }
}
