using Ten99.D365.PluginRegistration.Tool.Model;

namespace Ten99.D365.PluginRegistration.Tool.Planning;

/// <summary>
/// Compares the declared registrations with what the environment has, and lists the changes that
/// would make the environment match. Only steps on this assembly's plugin types are considered.
/// </summary>
public static class Planner
{
	public static Plan Create(DeclaredAssembly declared, CurrentState current, bool prune)
	{
		var changes = new List<Change>();
		var warnings = new List<string>();
		var errors = new List<string>();
		var managed = new List<(DeclaredStep, CurrentStep?)>();

		PlanAssembly(declared, current, changes);

		var currentTypes = current.Types.ToDictionary(t => t.TypeName, StringComparer.Ordinal);
		var declaredTypeNames = declared.Types.Select(t => t.TypeName).ToHashSet(StringComparer.Ordinal);

		foreach (var type in declared.Types)
		{
			currentTypes.TryGetValue(type.TypeName, out var currentType);
			if (currentType is null)
			{
				changes.Add(new TypeChange(ChangeKind.Create, type.TypeName, null));
			}

			var currentSteps = currentType is null
				? []
				: current.Steps.Where(s => s.PluginTypeId == currentType.Id).ToList();

			PlanSteps(type, currentSteps, current.Steps, prune, changes, warnings, errors, managed);
		}

		//	Types registered in the environment that the new assembly no longer contains
		foreach (var orphan in current.Types.Where(t => !declaredTypeNames.Contains(t.TypeName)))
		{
			var orphanSteps = current.Steps.Where(s => s.PluginTypeId == orphan.Id).ToList();
			if (!prune)
			{
				errors.Add($"type {orphan.TypeName} is registered but no longer in the assembly, so the assembly can't be updated. Re-run with --prune to remove it{(orphanSteps.Count > 0 ? $" and its {orphanSteps.Count} step(s)" : "")}.");
				continue;
			}

			foreach (var step in orphanSteps)
			{
				changes.Add(new StepChange(ChangeKind.Delete, null, step, []));
			}

			changes.Add(new TypeChange(ChangeKind.Delete, orphan.TypeName, orphan));
		}

		return new Plan(declared, changes, warnings, errors) { ManagedSteps = managed };
	}

	private static void PlanAssembly(DeclaredAssembly declared, CurrentState current, List<Change> changes)
	{
		if (current.Assembly is null)
		{
			changes.Add(new AssemblyChange(ChangeKind.Create, declared, null, [$"version {declared.Version}"]));
			return;
		}

		var details = new List<string>();
		if (current.Assembly.Version != declared.Version)
		{
			details.Add($"version: {current.Assembly.Version} -> {declared.Version}");
		}

		if (current.Assembly.Content is null || !current.Assembly.Content.AsSpan().SequenceEqual(declared.Content))
		{
			details.Add("content changed");
		}

		if (details.Count > 0)
		{
			changes.Add(new AssemblyChange(ChangeKind.Update, declared, current.Assembly, details));
		}
	}

	private static void PlanSteps(
		DeclaredType type,
		List<CurrentStep> currentSteps,
		IReadOnlyList<CurrentStep> allSteps,
		bool prune,
		List<Change> changes,
		List<string> warnings,
		List<string> errors,
		List<(DeclaredStep, CurrentStep?)> managed)
	{
		var unmatchedCurrent = currentSteps.ToList();
		var matches = new List<(DeclaredStep Declared, CurrentStep? Current)>();
		var unmatchedDeclared = new List<DeclaredStep>();

		//	1) Pinned by Id
		foreach (var step in type.Steps)
		{
			if (step.Id is not Guid id)
			{
				unmatchedDeclared.Add(step);
				continue;
			}

			var match = unmatchedCurrent.FirstOrDefault(c => c.Id == id);
			if (match is not null)
			{
				unmatchedCurrent.Remove(match);
				matches.Add((step, match));
			}
			else if (allSteps.Any(c => c.Id == id))
			{
				errors.Add($"{step.Name}: Id {id} belongs to a step on a different plugin type.");
			}
			else
			{
				//	Created with the declared Id, so it's the same across environments
				matches.Add((step, null));
			}
		}

		//	2) By message, entity and stage. Name breaks ties when a class has several such steps.
		foreach (var group in unmatchedDeclared.GroupBy(Signature))
		{
			var declaredGroup = group.ToList();
			var currentGroup = unmatchedCurrent.Where(c => Signature(c) == group.Key).ToList();

			if (declaredGroup.Count == 1 && currentGroup.Count == 1)
			{
				unmatchedCurrent.Remove(currentGroup[0]);
				matches.Add((declaredGroup[0], currentGroup[0]));
				continue;
			}

			foreach (var step in declaredGroup)
			{
				var byName = currentGroup.FirstOrDefault(c => string.Equals(c.Name, step.Name, StringComparison.OrdinalIgnoreCase));
				if (byName is not null)
				{
					currentGroup.Remove(byName);
					unmatchedCurrent.Remove(byName);
				}

				matches.Add((step, byName));
			}
		}

		foreach (var (declared, current) in matches)
		{
			if (current is null)
			{
				changes.Add(new StepChange(ChangeKind.Create, declared, null, [.. DescribeNewStep(declared)]));
			}
			else
			{
				var details = DiffStep(declared, current);
				if (details.Count > 0)
				{
					changes.Add(new StepChange(ChangeKind.Update, declared, current, details));
				}
			}

			PlanImages(declared, current?.Images ?? [], changes);
			managed.Add((declared, current));
		}

		//	3) Registered steps nobody declared
		foreach (var step in unmatchedCurrent)
		{
			if (prune)
			{
				changes.Add(new StepChange(ChangeKind.Delete, null, step, []));
			}
			else
			{
				warnings.Add($"step {step.Name} [{step.Stage}, {step.Mode}] ({step.Id}) is registered but not declared. It is left as is. Re-run with --prune to remove it.");
			}
		}
	}

	private static void PlanImages(DeclaredStep step, IReadOnlyList<CurrentImage> currentImages, List<Change> changes)
	{
		var unmatched = currentImages.ToList();

		foreach (var image in step.Images)
		{
			var match = unmatched.FirstOrDefault(c => c.Type == image.Type && string.Equals(c.Alias, image.Alias, StringComparison.OrdinalIgnoreCase))
				?? unmatched.FirstOrDefault(c => c.Type == image.Type);

			if (match is null)
			{
				changes.Add(new ImageChange(ChangeKind.Create, step, image, null, [$"attributes: {Format(image.Attributes)}"]));
				continue;
			}

			unmatched.Remove(match);

			var details = new List<string>();
			AddIfDifferent(details, "alias", match.Alias, image.Alias);
			AddIfDifferent(details, "name", match.Name, image.Name);
			if (!SameSet(match.Attributes, image.Attributes))
			{
				details.Add($"attributes: {Format(match.Attributes)} -> {Format(image.Attributes)}");
			}

			if (details.Count > 0)
			{
				changes.Add(new ImageChange(ChangeKind.Update, step, image, match, details));
			}
		}

		//	A declared step owns its images, so undeclared ones go
		foreach (var image in unmatched)
		{
			changes.Add(new ImageChange(ChangeKind.Delete, step, null, image, []));
		}
	}

	private static List<string> DiffStep(DeclaredStep declared, CurrentStep current)
	{
		var details = new List<string>();
		AddIfDifferent(details, "message", current.Message, declared.Message);
		AddIfDifferent(details, "entity", current.Entity ?? "none", declared.Entity ?? "none");
		AddIfDifferent(details, "stage", current.Stage.ToString(), declared.Stage.ToString());
		AddIfDifferent(details, "mode", current.Mode.ToString(), declared.Mode.ToString());
		AddIfDifferent(details, "order", current.Order.ToString(), declared.Order.ToString());
		AddIfDifferent(details, "name", current.Name, declared.Name);

		if (!SameSet(current.FilteringAttributes, declared.FilteringAttributes))
		{
			details.Add($"filtering attributes: {Format(current.FilteringAttributes)} -> {Format(declared.FilteringAttributes)}");
		}

		return details;
	}

	private static IEnumerable<string> DescribeNewStep(DeclaredStep step)
	{
		if (step.Id is Guid id) yield return $"id {id}";
		if (step.Order != 1) yield return $"order {step.Order}";
		if (step.FilteringAttributes is not null) yield return $"filtering attributes: {Format(step.FilteringAttributes)}";
	}

	private static (string, string?, Stage) Signature(DeclaredStep step) => (step.Message.ToLowerInvariant(), step.Entity, step.Stage);

	private static (string, string?, Stage) Signature(CurrentStep step) => (step.Message.ToLowerInvariant(), step.Entity, step.Stage);

	private static void AddIfDifferent(List<string> details, string field, string current, string declared)
	{
		if (!string.Equals(current, declared, StringComparison.Ordinal))
		{
			details.Add($"{field}: {current} -> {declared}");
		}
	}

	private static bool SameSet(IReadOnlyList<string>? current, IReadOnlyList<string>? declared) =>
		(current is null || current.Count == 0) && (declared is null || declared.Count == 0)
		|| current is not null && declared is not null
			&& current.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(declared);

	internal static string Format(IReadOnlyList<string>? attributes) =>
		attributes is null || attributes.Count == 0 ? "(all)" : string.Join(",", attributes);
}
