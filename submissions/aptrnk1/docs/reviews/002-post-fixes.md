# Review: `git diff main...HEAD` (aptrnk1) against AGENTS.md

**Gate run by me:** `dotnet build -warnaserror` gave 0 errors. `dotnet test` passed 34 tests in Core and 20 in Cli, 0 failed, exit 0. Two of those 54 are empty placeholder tests (finding 10).

All the bugs and risks from review 001 (#1 to #8) are fixed and have tests. The findings below are new or left over.

## Bugs

1. **`src/AgentLog.Core/ExitStatusJsonConverter.cs:19,28`: a numeric `exit` that doesn't fit in an `int` drops the whole line.** When `TryGetInt32` fails, the code falls through to `default` and throws `JsonException`. The parser then skips the line. The hook writes `Number(m[1])` from `/^Exit code (\d+)/` (log-action.mjs:60), which has no upper limit.
   - **Failure scenario:** on Windows a crashed process reports `Exit code 3221225477` (0xC0000005). That `PostToolUseFailure` line becomes an "unparsable line". Its Pre line then has no Post, so it shows up as **blocked** instead of **failed**, and `failed` doesn't list it. This breaks the converter's own rule ("one odd value does not drop the whole line", line 8).
   - **Fix:** use `TryGetInt64`, or map out-of-range numbers to `ExitKind.Error`. Add a test with `"exit":3221225477`.

2. **`src/AgentLog.Core/Aggregator.cs:150-165`: a long-running call is reported as "blocked" when other calls in the same session finish while it runs.** "Pending" means only "nothing executed after this Pre line in the session". It doesn't look at whether a call is still running.
   - **Failure scenario:** the log has `Pre Agent(id=A)`, then the subagent's own `Pre/Post Read` lines (same `session`), and `Post A` hasn't been written yet. Running `agentlog blocked` (for example, from inside that subagent) lists the parent's `Agent` call as blocked.
   - **Same with parallel calls:** with `Pre Bash(dotnet test)`, `Pre Read`, `Post Read`, the slow Bash is counted as blocked until it finishes.
   - **The reverse case:** a denial that is the last action of a session (the user rejects it and quits) stays "pending" forever and never shows in `blocked`.
   - No test covers either case.
   - **Fix:** at minimum, document this in the `blocked` help text. Add a test with interleaved lines that states the intended result.

## Risks

3. **`tests/AgentLog.Cli.Tests/CliEndToEndTests.cs:212-224`: the CLI `--session` wiring is not really tested.**
   - `Session_filter_with_unknown_session_gives_empty_report` runs `blocked` on the fixture, which already prints "No blocked actions." with no filter.
   - `Session_filter_with_known_session_keeps_everything` compares output that is identical when the filter is ignored.
   - **Failure scenario:** if `CliApp.cs:81` passed `null` instead of `parse.GetValue(session)`, both tests would still pass.
   - **Fix:** run `summary --session deadbeef` and assert `0 executed ... 0 session(s)`.

4. **`tests/AgentLog.Cli.Tests/CliEndToEndTests.cs:258-280`: the unreadable-file test fails when tests run as root.** `chmod 000` doesn't stop root from reading the file.
   - **Failure scenario:** in a Docker CI image running as root, the CLI prints a summary and returns 0, so `Assert.Equal(3, ...)` fails. The gate goes red with nothing wrong in the code.
   - **Fix:** skip the test when `Environment.UserName == "root"`, or check whether the file can actually be opened before asserting.

5. **`src/AgentLog.Cli/CliApp.cs:62-68`: "missing file comes first" only holds for errors this code checks itself.** System.CommandLine rejects parse errors before the action runs.
   - **Failure scenario 1:** `agentlog no/such.jsonl --format xml` returns 1 with a usage error, not 2.
   - **Failure scenario 2:** a typo such as `agentlog blokced` is read as a file path and returns 2 with "No log at blokced. Are the hooks ... active?". That hint is misleading.
   - The 2 = missing file contract still holds for valid arguments. The comment on line 62 promises more than the code does.

6. **`src/AgentLog.Core/LogLineParser.cs:18`: a `ts` without an offset is read in the machine's local time zone.**
   - **Failure scenario:** a hand-edited line `"ts":"2026-10-04T10:00:00"` parses as 10:00 local time. `--since`, `first`/`last` and the sort order then depend on the host time zone. This bypasses the `TimeProvider` rule in AGENTS.md (it isn't `DateTime.Now`, but it is still hidden local-clock state in Core).
   - The hook always writes `Z`, so only edited or foreign logs are affected.
   - **Fix:** reject `ts` values without an offset, or read them as UTC, the same way `TryParseSince` does.

7. **`src/AgentLog.Core/Aggregator.cs:61,72,142-144`: every event that isn't `PreToolUse` counts as executed** (left over from review 001 #3). The hook writes `"event":"unknown"` when `hook_event_name` is missing (log-action.mjs:24).
   - **Failure scenario:** such a line adds 1 to `executed`. If it has an `id`, it also hides a real block.
   - **Fix:** count only `PostToolUse` and `PostToolUseFailure` as executed. Add a test.

8. **Some behaviours have no test that would catch a regression:**
   - **JSON output for string exits:** `Failed_as_json_keeps_exit_shape` covers only `exit: 1`. If `ExitStatusJsonConverter.Write` (`:40-45`) wrote `"error"` for `Interrupted`, no test would fail.
   - **The `--since` boundary** (left over from 001 #10): no line has `ts == since`, so changing `&gt;=` to `&gt;` in `LogFilter.cs:12` passes every test.
   - **Windows paths and empty-file text output** (left over from 001 #10): there is a JSON test for the empty file but no text test, and `D:/x` vs `d:/x` grouping is untested.

## Nits

9. **`src/AgentLog.Core/RoundedMsJsonConverter.cs:13`:** `(int)Math.Round(...)` is unchecked. On net8.0 an `ms` above `int.MaxValue` becomes `int.MinValue`, which makes `TotalMs` negative. This is unrealistic with real hook data, but `checked` or clamping costs nothing.

10. **`tests/*/UnitTest1.cs`: the empty placeholder tests are still there** (left over from 001 #9). The quoted 54 includes 2 no-ops.

11. **`src/AgentLog.Core/LogFilter.cs:27`: `--since` accepts dates in the invariant `MM/dd` format.** `04/10/2026` silently means April 10, and a European user would mean October 4. Also, `--since 2026-10-04` is midnight UTC while `today` is local midnight. That is documented, but easy to trip over.

12. **`src/AgentLog.Core/ReportFormatter.cs:92`:** only `\r\n` and `\n` are escaped. A lone `\r` or a tab in `cmd` still breaks the alignment of the text table.

13. **`src/AgentLog.Cli/CliApp.cs:85`:** the comment says `ReadLines` is lazy, but in .NET it opens the file immediately. The behaviour is correct because the call is inside the `try`. Only the comment is wrong.

14. **Process items:**
    - The fix commits (`a2984a8`, `2b0aeab`, `7357830`, `214d8c3`, `e979530`, `903ed9e`) each put the test and the fix in one commit. Nothing shows the failing test came first, as "Bugs get a failing test first" requires.
    - `6e4ab05` is still not a Conventional Commit.
    - `docs/evidence.md:8` leaves the SessionStart check after a Claude restart as a TODO, and it has no quote of the final green gate.

15. **Left over from 001 #13 and #14:**
    - The test projects reference packages outside the allowed list (`coverlet.collector`, `Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio`).
    - The `.claude/skills/vercel-react-best-practices/**` React skill (about 3,800 lines) has nothing to do with this .NET repo.

## What I checked
- **Core purity:** there is no `File`, `Console` or `DateTime.Now` in `src/AgentLog.Core`, and `TimeProvider` is used for "today". Data types are records, and the only statics are read-only.
- **Log-format edge cases:**
  - malformed and blank lines
  - missing `id`
  - `exit` as an int, a known string, an unknown string, missing, or an out-of-range number
  - fractional `ms`
  - `ts` without an offset
  - Windows paths
  - empty file
  - pending vs blocked
- **CLI contract:** exit codes 0, 1, 2 and 3, in what order they are decided, and whether `--format json` always writes the same keys.
- **Test strength:** whether each assertion would fail if the code were wrong.

## Verdict
**Approve with changes.** The architecture rules are followed and the gate is green. Fix before merging:
- **#1:** a Windows crash code silently drops the line and turns a failure into a "blocked" entry.
- **#3:** the session-filter e2e tests prove nothing.
- **#4:** the unreadable-file test fails when CI runs as root.

At least document #2 (calls that are still running get counted as blocked). The rest can go in follow-ups.

Relevant files:
- /Users/andrii/fw-agentic-engineering-course/2026-agentic-engineering-crash-course-capstone/submissions/aptrnk1/src/AgentLog.Core/ExitStatusJsonConverter.cs
- /Users/andrii/fw-agentic-engineering-course/2026-agentic-engineering-crash-course-capstone/submissions/aptrnk1/src/AgentLog.Core/Aggregator.cs
- /Users/andrii/fw-agentic-engineering-course/2026-agentic-engineering-crash-course-capstone/submissions/aptrnk1/src/AgentLog.Core/LogLineParser.cs
- /Users/andrii/fw-agentic-engineering-course/2026-agentic-engineering-crash-course-capstone/submissions/aptrnk1/src/AgentLog.Cli/CliApp.cs
- /Users/andrii/fw-agentic-engineering-course/2026-agentic-engineering-crash-course-capstone/submissions/aptrnk1/tests/AgentLog.Cli.Tests/CliEndToEndTests.cs
