using System.Text;
using AgentLog.Cli;

Console.OutputEncoding = Encoding.UTF8;
return CliApp.Run(args, Console.Out, Console.Error, TimeProvider.System);
