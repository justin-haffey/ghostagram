# Prompt: Create a new Codex Agent

Create a specialized new Codex sub-agent that can be spawned from the Codex CLI, extension or API. This agent should be designed to perform a specific set of tasks or functions, and should be able to operate independently within the Codex ecosystem. The agent should be able to interact with other agents and tools as needed, and should be able to provide useful outputs based on its designated purpose.

## Inputs

Use the inputs provided below to guide the creation of the new agent. These inputs will help define the agent's purpose, tasks, work style, and other important characteristics that will shape its design and functionality. Be sure to consider each input carefully and use it to inform your decisions throughout the agent creation process.

Note: If the inputs are not provided, you can use the following `Input schema` and collect information from the user necessary information to create the agent:

{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://example.com/schemas/agent-definition.schema.json",
  "title": "Agent Definition",
  "description": "Schema for defining an AI agent's identity, behavior, capabilities, and constraints.",
  "type": "object",
  "additionalProperties": false,
  "required": [
    "agentName",
    "primaryPurpose",
    "workStyle",
    "mainTasks",
    "nonGoals",
    "preferredModelBehavior",
    "sandboxPreference",
    "toolsOrIntegrations",
    "skillsNeeded"
  ],
  "properties": {
    "agentName": {
      "type": "string",
      "minLength": 1,
      "description": "The canonical name of the agent.",
      "examples": ["DataSynth Architect"]
    },
    "primaryPurpose": {
      "type": "string",
      "minLength": 10,
      "description": "A clear, concise statement describing the agent's primary objective or mission.",
      "examples": ["Design and optimize data pipelines for enterprise AI workloads."]
    },
    "workStyle": {
      "type": "string",
      "enum": ["read-heavy", "write-heavy", "mixed"],
      "description": "Indicates whether the agent primarily consumes information, produces outputs, or balances both."
    },
    "mainTasks": {
      "type": "array",
      "description": "Core responsibilities the agent is expected to perform.",
      "items": {
        "type": "string",
        "minLength": 3
      },
      "minItems": 1,
      "uniqueItems": true,
      "examples": [
        ["Analyze logs", "Generate reports", "Summarize documents"]
      ]
    },
    "nonGoals": {
      "type": "array",
      "description": "Explicit boundaries or tasks the agent must avoid.",
      "items": {
        "type": "string",
        "minLength": 3
      },
      "minItems": 1,
      "uniqueItems": true,
      "examples": [
        ["Do not execute code", "Avoid making financial decisions"]
      ]
    },
    "preferredModelBehavior": {
      "type": "string",
      "enum": ["speed-first", "balanced", "deep-reasoning"],
      "description": "Desired tradeoff between latency and reasoning depth."
    },
    "sandboxPreference": {
      "type": "string",
      "enum": [
        "inherit",
        "read-only",
        "workspace-write",
        "danger-full-access"
      ],
      "description": "Defines the execution environment constraints for the agent."
    },
    "toolsOrIntegrations": {
      "description": "External tools or integrations required by the agent.",
      "oneOf": [
        {
          "type": "string",
          "const": "none",
          "description": "No tools or integrations are required."
        },
        {
          "type": "array",
          "description": "List of tools or services the agent depends on.",
          "items": {
            "type": "string",
            "minLength": 2
          },
          "minItems": 1,
          "uniqueItems": true,
          "examples": [
            ["Azure OpenAI", "Microsoft Graph", "Slack API"]
          ]
        }
      ]
    },
    "skillsNeeded": {
      "description": "Skills or competencies required for the agent to perform effectively.",
      "oneOf": [
        {
          "type": "string",
          "const": "none",
          "description": "No specific skills required."
        },
        {
          "type": "array",
          "items": {
            "type": "string",
            "minLength": 2
          },
          "minItems": 1,
          "uniqueItems": true,
          "examples": [
            ["Prompt engineering", "Data modeling", "API integration"]
          ]
        }
      ]
    },
    "nicknameCandidates": {
      "type": "array",
      "description": "Optional alternative names or aliases for the agent.",
      "items": {
        "type": "string",
        "minLength": 1
      },
      "uniqueItems": true,
      "examples": [
        ["SynthBot", "DataWizard"]
      ]
    },
    "extraConstraints": {
      "type": "string",
      "description": "Additional operational, ethical, or performance constraints not covered elsewhere.",
      "examples": [
        "Must comply with GDPR and avoid storing personal data."
      ]
    }
  }
}

## Steps to Create the Agent:

Follow the process in sequential steps to create the agent, ensuring that each step is completed thoroughly before moving on to the next. The steps should be designed to guide you through the process of researching, designing, and implementing the new agent based on the provided inputs.

1. Research and determine the best skills and philosophies for the agent based on its primary purpose and main tasks.

- Analyze the agent's primary purpose and main tasks to identify the key skills that will enable it to perform effectively.
- Consider the agent's work style and preferred model behavior to determine the most suitable philosophies that will guide its decision-making and interactions.
- Take into account any non-goals or boundaries specified in the inputs to ensure that the agent operates within the defined scope and does not engage in activities that are outside of its intended purpose.

2. Determine the necessary tools and integrations that the agent will require to perform its tasks effectively.

- Research the available tools and integrations (including mcp servers/tools) within the vscode and Codex ecosystem that align with the agent's purpose and tasks.
- Ensure that the selected tools and integrations are compatible with the agent's preferred model behavior and sandbox preference.
- Consider any additional constraints or requirements specified in the inputs when selecting tools and integrations.
- Document the selected tools and integrations, including how they will be used by the agent to achieve its goals.
- Include tools and integrations in the agent's configuration to enable seamless interaction and functionality.

3. Leverage the meta prompt `.codex/meta/meta-create-agent.prompt.md` to generate the agent's configuration and implementation details. Use the inputs and research from the previous steps to fill in the necessary information in the meta prompt, ensuring that the agent's configuration is comprehensive and aligned with its designated purpose and tasks.

## Output

1. Create the agent configuration .toml file in the appropriate directory within the Codex ecosystem, ensuring that it is properly formatted and includes all necessary information for the agent to function effectively. The output should include the agent's name, description, argument hints, model, tools, and any other relevant details that will enable it to perform its designated tasks and interact with other agents and tools as needed.

2. Update the AGENTS.md file to include the new agent, providing a brief description and any relevant details that will help users understand the agent's purpose and capabilities. This will ensure that the new agent is discoverable and can be easily accessed by users within the Codex ecosystem.