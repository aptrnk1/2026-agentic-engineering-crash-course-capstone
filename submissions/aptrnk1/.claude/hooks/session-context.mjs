#!/usr/bin/env node
// SessionStart hook: injects today's agentlog summary into the agent's context.
// Silent no-op until the CLI has been built. Never fails the session.
import { execFileSync } from "node:child_process";
import { existsSync } from "node:fs";
import { join } from "node:path";

const root = process.env.CLAUDE_PROJECT_DIR || process.cwd();
const dll = join(root, "src/AgentLog.Cli/bin/Debug/net10.0/agentlog.dll");
if (!existsSync(dll)) process.exit(0);
try {
  const md = execFileSync("dotnet", [dll, "summary", "--since", "today", "--format", "md"], {
    cwd: root, encoding: "utf8", timeout: 10000,
  });
  console.log(JSON.stringify({
    hookSpecificOutput: {
      hookEventName: "SessionStart",
      additionalContext: `What you (the agent) did in this repo today, from .agent-log:\n${md}`,
    },
  }));
} catch { /* context is a bonus, never a blocker */ }
process.exit(0);
