using System.CommandLine;
using Ten99.D365.PluginRegistration.Tool.Dataverse;
using Ten99.D365.PluginRegistration.Tool.Planning;
using Ten99.D365.PluginRegistration.Tool.Reading;

namespace Ten99.D365.PluginRegistration.Tool.Commands;

internal static class PlanCommand
{
	public const int ExitChanges = 2;

	public static Command Create()
	{
		var options = new CommonOptions();
		var detailedExitCode = new Option<bool>("--detailed-exitcode")
		{
			Description = $"Exit with {ExitChanges} when the plan has changes, 0 when it has none.",
		};

		var command = new Command("plan", "Show the changes that apply would make, without making them.");
		options.AddTo(command);
		command.Options.Add(detailedExitCode);

		command.SetAction(async (parseResult, cancellationToken) =>
		{
			var plan = await BuildPlanAsync(parseResult, options, cancellationToken);
			if (plan is null) return 1;

			if (!plan.Value.Plan.IsValid) return 1;
			return parseResult.GetValue(detailedExitCode) && plan.Value.Plan.HasChanges ? ExitChanges : 0;
		});

		return command;
	}

	/// <summary>Reads the assembly and the environment, then prints the plan. Null when the declarations are invalid.</summary>
	internal static async Task<(Plan Plan, Microsoft.PowerPlatform.Dataverse.Client.ServiceClient Client)?> BuildPlanAsync(
		ParseResult parseResult, CommonOptions options, CancellationToken cancellationToken)
	{
		var errors = new List<string>();
		var declared = AssemblyReader.Read(parseResult.GetValue(options.Assembly)!.FullName, errors);

		if (errors.Count > 0)
		{
			Console.Error.WriteLine($"{declared.Name} has invalid registration attributes:");
			foreach (var error in errors)
			{
				Console.Error.WriteLine($"  ! {error}");
			}

			return null;
		}

		var environment = Connection.ParseEnvironment(parseResult.GetValue(options.Environment)!);
		var credential = Connection.CreateCredential(parseResult.GetValue(options.Auth), parseResult.GetValue(options.Tenant));
		var client = Connection.Connect(environment, credential);

		var current = await new RegistrationReader(client).ReadAsync(declared.Name, cancellationToken);
		var plan = Planner.Create(declared, current, parseResult.GetValue(options.Prune));

		PlanPrinter.Print(plan, environment, Console.Out);
		return (plan, client);
	}
}
