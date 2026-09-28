using System.CommandLine;
using Ten99.D365.PluginRegistration.Tool.Applying;
using Ten99.D365.PluginRegistration.Tool.Dataverse;
using Ten99.D365.PluginRegistration.Tool.Reading;

namespace Ten99.D365.PluginRegistration.Tool.Commands;

internal static class PruneCommand
{
	public static Command Create()
	{
		var options = new CommonOptions();
		var whatIf = new Option<bool>("--what-if")
		{
			Description = "List the steps that would be deleted, without deleting them.",
		};

		var command = new Command("prune", "Delete registered steps the assembly no longer declares. Leaves the assembly, plugin types and declared steps alone.")
		{
			options.Assembly, options.Environment, options.Auth, options.Tenant, whatIf,
		};

		command.SetAction(async (parseResult, cancellationToken) =>
		{
			var errors = new List<string>();
			var declared = AssemblyReader.Read(parseResult.GetValue(options.Assembly)!.FullName, errors);
			if (errors.Count > 0)
			{
				Console.Error.WriteLine($"{declared.Name} has invalid registration attributes:");
				foreach (var error in errors) Console.Error.WriteLine($"  ! {error}");
				return 1;
			}

			var environment = Connection.ParseEnvironment(parseResult.GetValue(options.Environment)!);
			var client = Connection.Connect(environment, Connection.CreateCredential(parseResult.GetValue(options.Auth), parseResult.GetValue(options.Tenant)));
			var current = await new RegistrationReader(client).ReadAsync(declared.Name, cancellationToken);

			var (deletable, managed) = Pruner.Select(declared, current);
			Console.WriteLine($"Prune {declared.Name} in {environment}");

			foreach (var step in managed)
			{
				Console.WriteLine($"  ! managed, not deleted: {step.Name} [{step.Stage}, {step.Mode}] ({step.Id}). Remove it through a solution upgrade.");
			}

			if (deletable.Count == 0)
			{
				Console.WriteLine("  nothing to prune.");
				return 0;
			}

			if (parseResult.GetValue(whatIf))
			{
				foreach (var step in deletable)
				{
					Console.WriteLine($"  - would delete step {step.Name} [{step.Stage}, {step.Mode}] ({step.Id})");
				}

				return 0;
			}

			await new Pruner(new DataverseRegistrationTarget(client), Console.Out).PruneAsync(deletable, cancellationToken);
			Console.WriteLine($"Pruned {deletable.Count} step(s).");
			return 0;
		});

		return command;
	}
}
