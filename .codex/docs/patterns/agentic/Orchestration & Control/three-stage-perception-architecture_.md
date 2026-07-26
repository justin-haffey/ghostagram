---
url: "https://agentic-patterns.com/patterns/three-stage-perception-architecture/"
title: "Three-Stage Perception Architecture - Awesome Agentic Patterns"
---

[Skip to content](https://agentic-patterns.com/patterns/three-stage-perception-architecture/#problem)

# Three-Stage Perception Architecture

## Problem

Complex AI agents often struggle with unstructured inputs and need a systematic way to process information before taking action. Without a clear separation of concerns, agents can become monolithic and difficult to debug, extend, or optimize. Additionally, mixing perception, processing, and action logic makes it hard to swap out components or scale different parts of the system independently.

## Solution

Implement a three-stage pipeline architecture that cleanly separates an agent's workflow into distinct phases:

01. **Perception Stage**: Handles all input gathering and normalization
02. Receives raw inputs (text, images, audio, structured data)
03. Performs initial processing (OCR, speech-to-text, format conversion)
04. Normalizes data into a common internal representation

05. **Processing Stage**: Performs reasoning and decision-making

06. Analyzes normalized inputs using appropriate models
07. Applies business logic and reasoning
08. Makes decisions about what actions to take
09. Can involve multiple sub-agents or reasoning steps

10. **Action Stage**: Executes decisions in the environment

11. Translates decisions into concrete actions
12. Interfaces with external systems and APIs
13. Handles error recovery and retries
14. Reports results back to the system

## Example

```python
class ThreeStageAgent:
    def __init__(self):
        self.perception = PerceptionPipeline()
        self.processor = ProcessingPipeline()
        self.action = ActionPipeline()

    async def run(self, raw_input):
        # Stage 1: Perception
        perceived_data = await self.perception.process(raw_input)

        # Stage 2: Processing
        decisions = await self.processor.analyze(perceived_data)

        # Stage 3: Action
        results = await self.action.execute(decisions)

        return results

class PerceptionPipeline:
    def __init__(self):
        self.handlers = {
            'text': TextHandler(),
            'image': ImageHandler(),
            'audio': AudioHandler(),
            'structured': StructuredDataHandler()
        }

    async def process(self, raw_input):
        input_type = self.detect_input_type(raw_input)
        handler = self.handlers[input_type]

        # Normalize to common format
        normalized = await handler.normalize(raw_input)

        # Extract features
        features = await handler.extract_features(normalized)

        return {
            'type': input_type,
            'normalized': normalized,
            'features': features,
            'metadata': handler.get_metadata(raw_input)
        }

class ProcessingPipeline:
    def __init__(self):
        self.reasoning_engine = ReasoningEngine()
        self.decision_maker = DecisionMaker()

    async def analyze(self, perceived_data):
        # Apply reasoning based on input type and features
        analysis = await self.reasoning_engine.reason(perceived_data)

        # Make decisions based on analysis
        decisions = await self.decision_maker.decide(
            analysis,
            context=self.get_context()
        )

        # Validate decisions
        validated = await self.validate_decisions(decisions)

        return validated

class ActionPipeline:
    def __init__(self):
        self.executors = {
            'api_call': APIExecutor(),
            'database': DatabaseExecutor(),
            'file_system': FileSystemExecutor(),
            'notification': NotificationExecutor()
        }

    async def execute(self, decisions):
        results = []

        for decision in decisions:
            executor = self.executors[decision.action_type]

            try:
                result = await executor.execute(decision)
                results.append({
                    'decision': decision,
                    'status': 'success',
                    'result': result
                })
            except Exception as e:
                # Handle errors gracefully
                recovery_result = await self.attempt_recovery(decision, e)
                results.append(recovery_result)

        return results

```

Action Stage

Processing Stage

Perception Stage

Executor Selection

Action Execution

Error Handling

Results

Reasoning Engine

Analysis

Decision Making

Validation

Action Plan

Type Detection

Raw Input

Normalization

Feature Extraction

Structured Data

Feedback Loop

## Benefits

- **Modularity**: Each stage can be developed, tested, and scaled independently
- **Flexibility**: Easy to swap implementations for different stages
- **Debugging**: Clear boundaries make it easier to identify where issues occur
- **Reusability**: Stages can be shared across different agent types
- **Scalability**: Different stages can be scaled based on their computational needs

## Trade-offs

**Pros:**
\- Clean separation of concerns
\- Easier to maintain and extend
\- Better error isolation
\- Enables specialized optimization per stage
\- Facilitates team collaboration (different teams per stage)

**Cons:**
\- Additional complexity for simple tasks
\- Potential latency from stage transitions
\- Requires careful interface design between stages
\- May introduce overhead for data transformation between stages

## References

- [Software Architecture Patterns](https://www.oreilly.com/library/view/software-architecture-patterns/9781491971437/)
- [Pipeline Pattern in ML Systems](https://ml-ops.org/content/mlops-principles)