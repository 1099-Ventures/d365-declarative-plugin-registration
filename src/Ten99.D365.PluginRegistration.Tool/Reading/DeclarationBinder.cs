using Ten99.D365.PluginRegistration.Tool.Model;

namespace Ten99.D365.PluginRegistration.Tool.Reading;

/// <summary>
/// Binds a plugin class's image attributes to its step attributes and rejects declarations
/// that can't work in Dataverse, before anything is sent to an environment.
/// </summary>
public static class DeclarationBinder
{
	public static DeclaredType Bind(string typeName, IReadOnlyList<RawStep> rawSteps, IReadOnlyList<RawImage> rawImages, ICollection<string> errors)
	{
		var images = rawSteps.Select(_ => new List<DeclaredImage>()).ToList();

		foreach (var key in rawSteps.Where(s => s.Key is not null).GroupBy(s => s.Key!, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
		{
			errors.Add($"{typeName}: step Key '{key.Key}' is used more than once.");
		}

		foreach (var duplicate in rawSteps
			.Where(s => s.Key is null)
			.GroupBy(s => (Message: s.Message.ToLowerInvariant(), Entity: s.Entity?.ToLowerInvariant(), s.Stage))
			.Where(g => g.Count() > 1))
		{
			errors.Add($"{typeName}: more than one {duplicate.Key.Message} step on {duplicate.Key.Entity ?? "any entity"} at {duplicate.Key.Stage}. Give each a Key.");
		}

		foreach (var image in rawImages)
		{
			var index = FindStep(typeName, rawSteps, image, errors);
			if (index < 0) continue;

			var step = rawSteps[index];
			ValidateImage(typeName, step, image, errors);
			images[index].Add(new DeclaredImage(image.Type, image.Alias ?? DefaultAlias(image.Type), Normalise(image.Attributes)));
		}

		for (var i = 0; i < rawSteps.Count; i++)
		{
			foreach (var alias in images[i].GroupBy(im => im.Alias, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
			{
				errors.Add($"{typeName}: image alias '{alias.Key}' is used more than once on the {rawSteps[i].Message} step.");
			}
		}

		var steps = rawSteps.Select((s, i) => new DeclaredStep(
			typeName,
			s.Message,
			s.Entity,
			s.Stage,
			s.Mode,
			s.Order,
			Normalise(s.FilteringAttributes),
			ParseId(typeName, s, errors),
			s.Key,
			images[i],
			string.IsNullOrWhiteSpace(s.Name) ? null : s.Name)).ToList();

		foreach (var step in steps)
		{
			ValidateStep(step, errors);
		}

		return new DeclaredType(typeName, steps);
	}

	public static string DefaultAlias(ImageType type) => type switch
	{
		ImageType.PreImage => "preImage",
		ImageType.PostImage => "postImage",
		_ => "image",
	};

	private static int FindStep(string typeName, IReadOnlyList<RawStep> steps, RawImage image, ICollection<string> errors)
	{
		if (image.Step is not null)
		{
			for (var i = 0; i < steps.Count; i++)
			{
				if (string.Equals(steps[i].Key, image.Step, StringComparison.OrdinalIgnoreCase)) return i;
			}

			errors.Add($"{typeName}: image '{image.Alias ?? DefaultAlias(image.Type)}' names Step '{image.Step}', but no step has that Key.");
			return -1;
		}

		var candidates = steps
			.Select((s, i) => (Step: s, Index: i))
			.Where(x => string.Equals(x.Step.Message, image.Message, StringComparison.OrdinalIgnoreCase))
			.ToList();

		switch (candidates.Count)
		{
			case 1:
				return candidates[0].Index;
			case 0:
				errors.Add($"{typeName}: image for message '{image.Message}' has no {image.Message} step to bind to.");
				return -1;
			default:
				errors.Add($"{typeName}: image for message '{image.Message}' is ambiguous: there are {candidates.Count} {image.Message} steps. Set Step to one of their Keys.");
				return -1;
		}
	}

	private static void ValidateImage(string typeName, RawStep step, RawImage image, ICollection<string> errors)
	{
		var alias = image.Alias ?? DefaultAlias(image.Type);
		var hasPre = image.Type is ImageType.PreImage or ImageType.Both;
		var hasPost = image.Type is ImageType.PostImage or ImageType.Both;

		if (!string.Equals(step.Message, image.Message, StringComparison.OrdinalIgnoreCase))
		{
			errors.Add($"{typeName}: image '{alias}' is for message '{image.Message}' but is bound to the {step.Message} step.");
		}

		if (hasPre && Is(step.Message, "Create"))
		{
			errors.Add($"{typeName}: image '{alias}': Create has no pre-image.");
		}

		if (hasPost && Is(step.Message, "Delete"))
		{
			errors.Add($"{typeName}: image '{alias}': Delete has no post-image.");
		}

		if (hasPost && step.Stage != Stage.PostOperation)
		{
			errors.Add($"{typeName}: image '{alias}': a post-image is only available on a PostOperation step, not {step.Stage}.");
		}
	}

	private static void ValidateStep(DeclaredStep step, ICollection<string> errors)
	{
		if (step.FilteringAttributes is { Count: > 0 } && !Is(step.Message, "Update"))
		{
			errors.Add($"{step.Name}: FilteringAttributes only apply to Update steps.");
		}

		if (step.Mode == Mode.Asynchronous && step.Stage != Stage.PostOperation)
		{
			errors.Add($"{step.Name}: Asynchronous steps must be PostOperation.");
		}

		if (string.IsNullOrWhiteSpace(step.Message))
		{
			errors.Add($"{step.TypeName}: a step has no message.");
		}
	}

	private static Guid? ParseId(string typeName, RawStep step, ICollection<string> errors)
	{
		if (step.Id is null) return null;
		if (Guid.TryParse(step.Id, out var id)) return id;

		errors.Add($"{typeName}: step Id '{step.Id}' is not a GUID.");
		return null;
	}

	/// <summary>Attribute lists compare as sets. Null means all attributes.</summary>
	internal static IReadOnlyList<string>? Normalise(IReadOnlyList<string>? attributes)
	{
		if (attributes is null || attributes.Count == 0) return null;

		return attributes
			.Where(a => !string.IsNullOrWhiteSpace(a))
			.Select(a => a.Trim().ToLowerInvariant())
			.Distinct()
			.OrderBy(a => a, StringComparer.Ordinal)
			.ToList();
	}

	private static bool Is(string message, string expected) => string.Equals(message, expected, StringComparison.OrdinalIgnoreCase);
}
