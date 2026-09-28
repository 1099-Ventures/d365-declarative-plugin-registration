using System.CommandLine;
using Ten99.D365.PluginRegistration.Tool.Commands;

var root = new RootCommand("Registers Dataverse plugin steps and images declared with Ten99.D365.PluginRegistration attributes.")
{
	PlanCommand.Create(),
	ApplyCommand.Create(),
	ExportCommand.Create(),
};

return await root.Parse(args).InvokeAsync();
