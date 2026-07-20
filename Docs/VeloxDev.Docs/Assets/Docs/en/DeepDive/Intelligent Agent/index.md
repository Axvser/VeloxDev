# Intelligent Agent Architecture

The agent system is built on the **Model A(F) Framework** (MAF), providing Function Calling capabilities to LLMs.

## Architecture

```
User Message → ChatClient (LLM) → Tool Calls → Workflow Scope
                                                 ├── Read node properties
                                                 ├── Create/delete nodes
                                                 ├── Connect/disconnect slots
                                                 └── Request user confirmation
```

## Safety Levels

Levels 0–3 control tool aggressiveness. Level 0 is fully automatic; Level 3 requires user confirmation for every mutation.
