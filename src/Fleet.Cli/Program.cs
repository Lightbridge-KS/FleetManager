using System.CommandLine;
using Fleet.Cli.Commands;
using Fleet.Cli.Services;

var configManager = new CliConfigManager();

var rootCommand = new RootCommand("fleet-ctl — Fleet Manager CLI");

rootCommand.Add(LoginCommand.Create(configManager));
rootCommand.Add(AgentsCommand.Create(configManager));
rootCommand.Add(AppsCommand.Create(configManager));
rootCommand.Add(AuditCommand.Create(configManager));

var config = new CommandLineConfiguration(rootCommand);
return await config.InvokeAsync(args);
