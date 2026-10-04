---
name: evidence-entry
description: Append one entry to docs/evidence.md. Use when the human says "запиши в evidence" or after a red test, a reviewer finding, a blocked action, or an agent mistake.
---
Append ONE row to the table in docs/evidence.md:
| date (YYYY-MM-DD) | what happened | proof (commit hash / file / command) | human decision |

- Get the hash with `git log -1 --format=%h`.
- If the event is about blocked/failed actions, run
  `dotnet run --project src/AgentLog.Cli -- blocked --since today --format md` and quote it under the table.
- Write the "human decision" column ONLY from what the human told you. If unknown, ask — never invent it.
- Never rewrite older rows.