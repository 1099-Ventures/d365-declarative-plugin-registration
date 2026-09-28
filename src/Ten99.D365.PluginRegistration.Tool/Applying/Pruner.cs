using Ten99.D365.PluginRegistration.Tool.Model;
using Ten99.D365.PluginRegistration.Tool.Planning;

namespace Ten99.D365.PluginRegistration.Tool.Applying;

/// <summary>
/// Removes registered steps that the assembly no longer declares, and nothing else. For environments that
/// receive the assembly through solutions, where apply would deploy it outside the release.
/// </summary>
public sealed class Pruner(IRegistrationTarget target, TextWriter log)
{
	/// <summary>The steps prune would delete, and the managed ones it can't.</summary>
	public static (IReadOnlyList<CurrentStep> Deletable, IReadOnlyList<CurrentStep> Managed) Select(DeclaredAssembly declared, CurrentState current)
	{
		var undeclared = Planner.Create(declared, current, prune: true).Changes
			.OfType<StepChange>()
			.Where(c => c.Kind == ChangeKind.Delete)
			.Select(c => c.Current!)
			.ToList();

		return (undeclared.Where(s => !s.IsManaged).ToList(), undeclared.Where(s => s.IsManaged).ToList());
	}

	public async Task PruneAsync(IReadOnlyList<CurrentStep> steps, CancellationToken cancellationToken)
	{
		foreach (var step in steps)
		{
			await target.DeleteStepAsync(step.Id, cancellationToken);
			log.WriteLine($"  deleted step {step.Name} [{step.Stage}, {step.Mode}] ({step.Id})");
		}
	}
}
