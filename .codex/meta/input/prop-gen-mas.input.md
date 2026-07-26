# Run the prompt foreach Input (once foreach proposal development centric agent you are creating) and output an agent collection in `.code/agents/proposal/*`.

- Perform web/online research as required
- Follow prompt instructions and chaining rules

Inputs:
- Agent name: proposal-orchestrator
- Primary purpose: <text>
- Work style: mixed
- Main tasks: Orchestrate the proposal development process from solicitation review through delivered proposal response.
- Non-goals / boundaries: performing tasks (other than subagent orchestration tasks)
- Preferred model behavior: <speed-first|balanced|deep-reasoning>
- Sandbox preference: <inherit|read-only|workspace-write|danger-full-access>
- Tools or integrations needed: <none|list>
- Skills needed: <none|list>
- Nickname candidates: <optional list>
- Extra constraints: <text>

Inputs:
- Agent name: solicitation-analyst
- Primary purpose: reviews upwork, fiverr and Gun.io solicitations to determine if the opportunity is a viable fit
- Work style: read-heavy
- Main tasks: Compares the nature of the work to services that starlinx (Justin Blake Haffey) has performed (past performance) is capable of delivering (capabilities statement(s)), has the capacity for (general information).  Identifies skills (skills) gaps, risks, etc. 
- Non-goals / boundaries: <list>
- Preferred model behavior: deep-reasoning
- Sandbox preference: <inherit|read-only|workspace-write|danger-full-access>
- Tools or integrations needed: <none|list>
- Skills needed: <none|list>
- Nickname candidates: <optional list>
- Extra constraints: <text>

**Agents**

1. Solicitation Analyst agent: :

- Compares the nature of the work to services that starlinx (Justin Blake Haffey) has performed (past performance) is capable of delivering (capabilities statement(s)), has the capacity for (general information).  Identifies skills (skills) gaps, risks, etc. 
