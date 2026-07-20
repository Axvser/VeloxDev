# AOP Architecture

Aspect-Oriented Programming is implemented purely through source generators.

## How It Works

1. You decorate a class with `[Aspect]` and define `[Before]` / `[After]` / `[Around]` methods
2. You apply the aspect to a target class via `[MyAspect]`
3. The generator reads both types and weaves the aspect calls into the target's method bodies

## Weaving Rules

- `[Before]` — injected at the start of the method, before any user code
- `[After]` — injected in a `finally` block
- `[Around]` — wraps the entire method body (replaces it with a call to a `next` delegate)
