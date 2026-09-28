using Ten99.D365.PluginRegistration.Tool.Model;
using Ten99.D365.PluginRegistration.Tool.Planning;

namespace Ten99.D365.PluginRegistration.Tool.Applying;

/// <summary>
/// Executes a plan. The order matters: Dataverse rejects assembly content that is missing a registered
/// plugin type, and a new plugin type can only be registered once the assembly contains it.
/// </summary>
public sealed class Applier(IRegistrationTarget target, TextWriter log)
{
	public async Task ApplyAsync(Plan plan, CurrentState current, string? solution, CancellationToken cancellationToken)
	{
		if (!plan.IsValid)
		{
			throw new InvalidOperationException("The plan has errors and can't be applied.");
		}

		var solutionName = solution is null ? null : await target.ResolveSolutionAsync(solution, cancellationToken);

		//	1) Removals first, so the new assembly content validates
		foreach (var change in plan.Changes.OfType<StepChange>().Where(c => c.Kind == ChangeKind.Delete))
		{
			await target.DeleteStepAsync(change.Current!.Id, cancellationToken);
			Log(change);
		}

		foreach (var change in plan.Changes.OfType<TypeChange>().Where(c => c.Kind == ChangeKind.Delete))
		{
			await target.DeleteTypeAsync(change.Current!.Id, cancellationToken);
			Log(change);
		}

		//	2) Assembly
		var assemblyId = current.Assembly?.Id;
		var assemblyChange = plan.Changes.OfType<AssemblyChange>().SingleOrDefault();
		if (assemblyChange?.Kind == ChangeKind.Create)
		{
			assemblyId = await target.CreateAssemblyAsync(plan.Assembly, cancellationToken);
			Log(assemblyChange);
		}
		else if (assemblyChange?.Kind == ChangeKind.Update)
		{
			await target.UpdateAssemblyAsync(assemblyId!.Value, plan.Assembly, cancellationToken);
			Log(assemblyChange);
		}

		//	3) Plugin types
		var typeIds = current.Types.ToDictionary(t => t.TypeName, t => t.Id, StringComparer.Ordinal);
		foreach (var change in plan.Changes.OfType<TypeChange>().Where(c => c.Kind == ChangeKind.Create))
		{
			typeIds[change.TypeName] = await target.CreateTypeAsync(assemblyId!.Value, change.TypeName, cancellationToken);
			Log(change);
		}

		//	4) Steps
		var stepIds = new Dictionary<DeclaredStep, Guid>(ReferenceEqualityComparer.Instance);
		foreach (var (step, existing) in plan.ManagedSteps)
		{
			if (existing is not null) stepIds[step] = existing.Id;
		}

		foreach (var change in plan.Changes.OfType<StepChange>().Where(c => c.Kind != ChangeKind.Delete))
		{
			var step = change.Declared!;
			var typeId = typeIds[step.TypeName];

			if (change.Kind == ChangeKind.Create)
			{
				stepIds[step] = await target.CreateStepAsync(typeId, step, cancellationToken);
			}
			else
			{
				await target.UpdateStepAsync(change.Current!.Id, typeId, step, cancellationToken);
			}

			Log(change);
		}

		//	5) Images
		foreach (var change in plan.Changes.OfType<ImageChange>())
		{
			switch (change.Kind)
			{
				case ChangeKind.Create:
					await target.CreateImageAsync(stepIds[change.Step], change.Step, change.Declared!, cancellationToken);
					break;
				case ChangeKind.Update:
					await target.UpdateImageAsync(change.Current!.Id, change.Step, change.Declared!, cancellationToken);
					break;
				default:
					await target.DeleteImageAsync(change.Current!.Id, cancellationToken);
					break;
			}

			Log(change);
		}

		//	6) Solution. Adding a component that is already there is harmless, so always ensure it.
		if (solutionName is not null && assemblyId is Guid id)
		{
			await target.AddAssemblyToSolutionAsync(id, solutionName, cancellationToken);
			foreach (var stepId in stepIds.Values)
			{
				await target.AddStepToSolutionAsync(stepId, solutionName, cancellationToken);
			}

			log.WriteLine($"  = assembly and {stepIds.Count} step(s) in solution {solutionName}");
		}
	}

	private void Log(Change change)
	{
		var verb = change.Kind switch
		{
			ChangeKind.Create => "created",
			ChangeKind.Update => "updated",
			_ => "deleted",
		};

		log.WriteLine($"  {verb} {change.Describe()}");
	}
}
