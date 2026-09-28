using System.CommandLine;
using Ten99.D365.PluginRegistration.Tool.Applying;
using Ten99.D365.PluginRegistration.Tool.Dataverse;

namespace Ten99.D365.PluginRegistration.Tool.Commands;

internal static class ApplyCommand
{
	public static Command Create()
	{
		var options = new CommonOptions();
		var solution = new Option<string?>("--solution", "-s")
		{
			Description = "Unmanaged solution (unique or display name) to add the assembly and steps to.",
		};

		var command = new Command("apply", "Make the environment match the declared registrations.");
		options.AddTo(command);
		command.Options.Add(solution);

		command.SetAction(async (parseResult, cancellationToken) =>
		{
			var result = await PlanCommand.BuildPlanAsync(parseResult, options, cancellationToken);
			if (result is not var (plan, current, client)) return 1;
			if (!plan.IsValid) return 1;

			var solutionName = parseResult.GetValue(solution);
			if (!plan.HasChanges && solutionName is null)
			{
				Console.WriteLine("Nothing to apply.");
				return 0;
			}

			Console.WriteLine();
			Console.WriteLine("Applying:");
			await new Applier(new DataverseRegistrationTarget(client), Console.Out).ApplyAsync(plan, current, solutionName, cancellationToken);
			Console.WriteLine("Apply complete.");
			return 0;
		});

		return command;
	}
}
