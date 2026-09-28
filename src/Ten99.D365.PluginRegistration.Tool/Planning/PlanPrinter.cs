namespace Ten99.D365.PluginRegistration.Tool.Planning;

public static class PlanPrinter
{
	public static void Print(Plan plan, Uri environment, TextWriter output)
	{
		output.WriteLine($"Plan for {plan.Assembly.Name} {plan.Assembly.Version} against {environment}");
		output.WriteLine();

		foreach (var error in plan.Errors)
		{
			output.WriteLine($"  ! error: {error}");
		}

		foreach (var warning in plan.Warnings)
		{
			output.WriteLine($"  ? {warning}");
		}

		if (plan.Errors.Count + plan.Warnings.Count > 0) output.WriteLine();

		foreach (var change in plan.Changes)
		{
			output.WriteLine($"  {Symbol(change.Kind)} {change.Describe()}");
			foreach (var detail in change.Details)
			{
				output.WriteLine($"      {detail}");
			}
		}

		if (plan.HasChanges) output.WriteLine();

		output.WriteLine(plan.IsValid
			? $"Plan: {Count(plan, ChangeKind.Create)} to add, {Count(plan, ChangeKind.Update)} to change, {Count(plan, ChangeKind.Delete)} to delete."
			: $"Plan has {plan.Errors.Count} error(s). Nothing can be applied until they're fixed.");
	}

	private static int Count(Plan plan, ChangeKind kind) => plan.Changes.Count(c => c.Kind == kind);

	private static string Symbol(ChangeKind kind) => kind switch
	{
		ChangeKind.Create => "+",
		ChangeKind.Update => "~",
		_ => "-",
	};
}
