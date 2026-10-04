# Evidence log — agentlog

| Дата | Що сталося | Доказ | Рішення людини |
|---|---|---|---|
| 2026-10-04 | Червоні тести парсера рядка логу (`LogLineParser.Parse` → `NotImplementedException`): 5 кейсів, 7 запусків. `dotnet build -warnaserror`: 0 Warning(s), 0 Error(s). `dotnet test`: AgentLog.Core.Tests — Failed: 7, Passed: 1, Total: 8; AgentLog.Cli.Tests — Passed: 1; exit code 1 (запуск без пайпа) | коміт `e18203a`, `tests/AgentLog.Core.Tests/LogLineParserTests.cs`, `dotnet test` | Закомітити червоні тести як `test: log line parsing (red)` |

---