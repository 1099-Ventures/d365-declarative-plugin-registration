using System.CommandLine;
using Ten99.D365.PluginRegistration.Tool.Dataverse;

namespace Ten99.D365.PluginRegistration.Tool.Commands;

/// <summary>Options shared by the commands that talk to an environment. Each command gets its own instances.</summary>
internal sealed class CommonOptions
{
	public Option<FileInfo> Assembly { get; } = new("--assembly", "-a")
	{
		Description = "Path to the compiled plugin assembly. Its referenced assemblies must be in the same folder.",
		Required = true,
	};

	public Option<string> Environment { get; } = new("--environment", "-e")
	{
		Description = "Dataverse environment URL, e.g. https://org.crm.dynamics.com.",
		Required = true,
	};

	public Option<AuthMode> Auth { get; } = new("--auth")
	{
		Description = "How to sign in: Interactive (browser), DeviceCode, or AzureCli (pipelines).",
		DefaultValueFactory = _ => AuthMode.Interactive,
	};

	public Option<string?> Tenant { get; } = new("--tenant")
	{
		Description = "Entra tenant id or domain. Defaults to the signed-in account's home tenant.",
	};

	public Option<bool> Prune { get; } = new("--prune")
	{
		Description = "Delete steps, images and plugin types that are registered but no longer declared.",
	};

	public void AddTo(Command command)
	{
		command.Options.Add(Assembly);
		command.Options.Add(Environment);
		command.Options.Add(Auth);
		command.Options.Add(Tenant);
		command.Options.Add(Prune);
	}
}
