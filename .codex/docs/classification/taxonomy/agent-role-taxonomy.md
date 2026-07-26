# High-level taxonomy (top-level headings)

Categorized AI Agent role taxonomy.

## 1. Orchestration & Control

**Definition**: central coordinator that decomposes goals, allocates tasks to other agents, sequences actions, and synthesizes results. The Orchestrator provides global strategy, failure handling, and resource allocation for a multi-agent system. (Often called Manager, Director, or Coordinator.)

### Roles (subtypes)

#### Orchestrator / Manager

- Responsibilities: take an overall objective, create a task plan (high-level steps), assign subtasks to specialist agents, collect and consolidate outputs, enforce constraints (budget, latency, safety).
- Inputs: user goal + system policy + available agent inventory.
- Outputs: task assignments, consolidated result, audit trail.
- Interaction pattern: synchronous planning cycle (plan → dispatch → collect → evaluate → iterate). Works by calling planning agents then executors.
- Metrics: throughput, success rate of composed tasks, latency, cost efficiency.
- Risks: overcentralization (single point of failure), brittle plans; mitigate with fallback planning, sharding, health checks.

#### Dispatcher / Load Balancer

- Lighter-weight than Orchestrator; routes tasks to available executors and balances cost/latency. Useful in production multi-agent pipelines.


## 2. Planning & Deliberation

**Definition**: agents that create plans, subgoals, strategies, and decision policies — converting high-level intent into ordered, testable steps. (Planner, Decomposer, Strategist.) Planning agents often produce sequences used by executors.

### Roles (subtypes)

#### Planner / Decomposer

- Responsibilities: hierarchical task decomposition, option generation, contingency planning.
- Inputs: objective, constraints, state snapshot.
- Outputs: ordered plan (with alternatives), estimated cost/time, dependency graph.
- Interaction: hands plan to Executor; may call Researcher agents for feasibility checks.
- Example: LangChain “plan-and-execute” pattern (Planner produces steps for Executor).

#### Policy / Decision Agent

- Produces action-selection policies (utility function or heuristic weights) — useful when many action alternatives exist or when tradeoffs must be optimized. Supported by utility-based agent theory.

## 3. Information & Research

**Definition**: agents that retrieve, filter, and synthesize external knowledge — authoritative sources, internal KBs, web search, or databases. (Retriever, SME, Researcher, Fact-finder.) Surveys show retrieval + role specialization are central to LLM-based MASs.

### Roles (subtypes)

#### Retriever / Search Agent

- Responsibilities: execute targeted searches (internal KG, vector DB, web), return ranked evidence and provenance.
- Inputs: query or evidence request.
- Outputs: ranked documents, snippets, metadata (timestamp, source).
- Interaction: called by Planner, Orchestrator, or Executor; supplies grounding material for decisions.
- Metrics: precision@k, recall, freshness, provenance accuracy.

#### Subject-Matter Expert (SME) / Domain Specialist

- Responsibilities: interpret retrieved evidence, apply domain rules, produce domain-correct answers (e.g., legal, medical, finance).
- Notes: SME agents are frequently small, constrained LLMs augmented with curated KBs or rule engines.

#### Citation & Provenance Agent

- Attaches formal citations, timestamps, and confidence scores; critical for auditability and RAG pipelines.

## 4. Execution & Tooling

**Definition**: agents that take concrete actions: API calls, database writes, code execution, form submission, or robotic control. They are the “hands” of the system. LangChain and many frameworks model agents that can call external tools.

### Roles (subtypes)

#### Executor / Action Agent

- Responsibilities: perform discrete actions defined by plan steps (run query, call API, manipulate documents).
- Inputs: atomic task + required parameters.
- Outputs: action result, logs, error codes.
- Interaction: called by Planner/Orchestrator or operate autonomously on a queue.
- Example: LangChain executor pattern (action agent chooses tools and handles retries).

#### Connector / Tool Agent

- Adapts external systems to the agent environment (DB adapter, email sender, browser controller). Provides consistent interface for Executors.

#### Batch Worker

- Executes large volumes of similar tasks (ETL, scraping) with idempotency and checkpointing.

## 5. Verification & Validation

**Definition**: agents that verify outputs for correctness, consistency, policy compliance, and safety. They provide guardrails and quality assurance within multi-agent workflows. Research highlights the need for validator/critic agents in complex MASs.

### Roles (subtypes)

#### Validator / Verifier

- Responsibilities: check factuality, consistency with rules, run test scenarios, perform unit checks on outputs.
- Inputs: candidate output + acceptance criteria.
- Outputs: pass/fail + diagnostics, suggestions for correction.
- Interaction: sits in feedback loop; may trigger re-planning or rerun executors.
- Metrics: false positive/negative rates, residual error, remediation cost.

#### Auditor / Compliance Agent

- Ensures regulatory and policy adherence, produces audit trails, flags non-compliant results.

#### Consensus / Voting Agent

- Increases robustness: ask multiple independent agents and use voting/aggregation to decide final output (useful in adversarial or high-risk domains).

## 6. Memory, Knowledge & State

**Definition**: agents that maintain, curate, and expose shared or private state (short-term memory, long-term knowledge graphs, term stores). They let agents reuse context, avoid repeated retrieval, and maintain continuity. Surveys of LLM-based MAS emphasize profile/state as a core component.

### Roles (subtypes)

#### Memory Manager

- Responsibilities: store conversation context, user preferences, and session artifacts; provide context windows for other agents.
- Interaction: used by Researcher, Planner, Executor, and Interface agents.
- Design choices: vector vs symbolic memory, TTL policies, privacy controls.
- Metrics: retrieval latency, relevance of recalled context, memory hit rate.

#### Knowledge Graph / KB Agent

- Manages canonical facts, relationships, and ontology; serves authoritative responses to SMEs and retrievers.

### 7. Synthesis & Communication

**Definition**: agents that convert technical outputs into human-facing artifacts: reports, explanations, summaries, or user messages. (Writer, Summarizer, Translator.) These agents prioritize clarity, formatting, and user intent alignment.

### Roles (subtypes)

#### Synthesizer / Composer

- Responsibilities: aggregate evidence, produce final narrative or artifact, format output for the target audience (executive summary, technical report).
- Inputs: validated facts, provenance, user preferences.
- Outputs: final deliverable (text, slide deck, email).
- Metrics: readability, task satisfaction, alignment with brief.

#### Translator / Localizer

- Adapts language/tone for audience, applies compliance language (disclaimers) where needed.

### 8. Monitoring, Safety & Governance

**Definition**: agents whose job is to observe system behavior, enforce policies, detect anomalous behavior, and intervene (kill switch, rate-limit). LLM-MAS literature stresses governance and safety agents to mitigate emergent risks.

### Roles (subtypes)

#### Watchdog / Monitor

- Responsibilities: runtime health monitoring, resource usage, anomaly detection.
- Outputs: alerts, metrics, automated throttling.
- Policy / Safety Agent
- Enforces content safety filters, disallow lists, privacy gating, and regulatory checks.

#### Incident Responder

- Coordinates rollback, root cause analysis, and human escalation.

## 9. Learning & Adaptation

**Definition**: agents that improve the system over time via retraining, feedback loops, online learning, or self-improvement (update models, tune prompts). Surveys note “evolution” as a core MAS component.

### Roles (subtypes)

#### Trainer / Updater

- Responsibilities: collect labeled feedback, retrain models or update prompts/policies, evaluate A/B tests.
- Metrics: model performance delta, drift detection, user satisfaction gains.

#### Meta-Learner

- Adjusts other agents’ hyperparameters or strategies (e.g., adapt planner heuristics to observed failure modes).

## 10. Integration / Interface

**Definition**: agents that handle user interaction and system integration: UI agents, chat frontends, API gateways, human-in-the-loop controllers. They mediate between humans and the agent fleet.

### Roles (subtypes)

#### Interface / Conversational Agent

- Responsibilities: present options to user, gather clarifications, and map user intent to system tasks.
- Design: must expose provenance and allow easy manual overrides.
- Human-in-the-Loop (HITL) Coordinator
- Routes approvals, escalations, and manual verification steps to human operators where necessary.