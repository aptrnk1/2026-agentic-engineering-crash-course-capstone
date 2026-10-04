# agentlog — project rules

.NET global tool that summarizes `.agent-log/actions.jsonl` (Claude Code hook log).

## Commands
- Check (the only gate): `dotnet build -warnaserror && dotnet test`
- Run: `dotnet run --project src/AgentLog.Cli -- summary [file]`

## Definition of done
- The check is green and you quote its output (test count, exit code) — evidence, not claims.
- Every new behaviour has a test. Bugs get a failing test first.

## Architecture
- `src/AgentLog.Core` is pure: NO `System.IO`, NO `Console`, NO `DateTime.Now`
  (use `TimeProvider`). It takes `IEnumerable<string>` lines.
- `src/AgentLog.Cli` is thin: parse args → read file → call Core → print.
- Data types are `record`s. No static mutable state.

## Log format (source of truth: .claude/hooks/log-action.mjs)
`{ts,event,id,session,mode,tool,path|cmd|pattern|url,exit,ms}`;
`exit` is int OR "interrupted"/"error"; blocked = PreToolUse with no Post line of the same id.

## Boundaries
- Never add NuGet packages without asking (allowed: System.CommandLine, xunit).
- Never edit `.claude/**`, `AGENTS.md`, `Directory.Build.props`, `global.json` — propose, human edits.
- Conventional Commits, one logical change per commit. English code and commits.