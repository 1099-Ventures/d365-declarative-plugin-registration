using System.Text;
using Ten99.D365.PluginRegistration.Tool.Model;
using Ten99.D365.PluginRegistration.Tool.Reading;

namespace Ten99.D365.PluginRegistration.Tool.Exporting;

/// <summary>
/// Writes the attributes that declare what is currently registered, so hand-registered steps can be
/// adopted: every step keeps its Id, and a plan over the pasted attributes comes out empty.
/// </summary>
public static class Exporter
{
	//	The constants the attribute package's Message class defines
	private static readonly HashSet<string> MessageConstants = new(StringComparer.Ordinal)
	{
		"Create", "Update", "Delete", "Retrieve", "RetrieveMultiple", "Associate", "Disassociate", "Assign", "SetState", "SetStateDynamicEntity",
	};

	public static string Export(CurrentState state, Uri environment)
	{
		var output = new StringBuilder();
		output.AppendLine($"// Registrations for {state.Assembly?.Name ?? "(not registered)"} exported from {environment}");
		output.AppendLine("// Paste each block onto its class. Requires: using Ten99.D365.PluginRegistration;");

		foreach (var type in state.Types.OrderBy(t => t.TypeName, StringComparer.Ordinal))
		{
			output.AppendLine();
			var steps = state.Steps
				.Where(s => s.PluginTypeId == type.Id)
				.OrderBy(s => s.Message, StringComparer.OrdinalIgnoreCase)
				.ThenBy(s => s.Stage)
				.ThenBy(s => s.Id)
				.ToList();

			if (steps.Count == 0)
			{
				output.AppendLine($"// {type.TypeName}: no steps registered");
				continue;
			}

			output.AppendLine($"// {type.TypeName}");
			var keys = AssignKeys(steps);

			foreach (var step in steps)
			{
				output.AppendLine(StepAttribute(type.TypeName, step, keys[step.Id]));
			}

			foreach (var step in steps)
			{
				foreach (var image in step.Images.OrderBy(i => i.Type).ThenBy(i => i.Alias, StringComparer.Ordinal))
				{
					output.AppendLine(ImageAttribute(step, image, keys[step.Id]));
				}
			}
		}

		return output.ToString();
	}

	/// <summary>A message with several steps on one class needs keys, so images can say which step they belong to.</summary>
	private static Dictionary<Guid, string?> AssignKeys(List<CurrentStep> steps)
	{
		var keys = new Dictionary<Guid, string?>();
		foreach (var group in steps.GroupBy(s => s.Message, StringComparer.OrdinalIgnoreCase))
		{
			if (group.Count() == 1)
			{
				keys[group.First().Id] = null;
				continue;
			}

			foreach (var byStage in group.GroupBy(s => s.Stage))
			{
				var index = 1;
				foreach (var step in byStage)
				{
					var key = byStage.Key.ToString().ToLowerInvariant();
					keys[step.Id] = byStage.Count() == 1 ? key : $"{key}{index++}";
				}
			}
		}

		return keys;
	}

	private static string StepAttribute(string typeName, CurrentStep step, string? key)
	{
		var args = new List<string> { MessageArgument(step.Message) };
		if (step.Entity is not null) args.Add(Literal(step.Entity));
		args.Add($"Stage.{step.Stage}");

		if (step.Mode != Mode.Synchronous) args.Add($"Mode = Mode.{step.Mode}");
		if (step.Order != 1) args.Add($"Order = {step.Order}");
		if (step.FilteringAttributes is { Count: > 0 }) args.Add($"FilteringAttributes = new[] {{ {string.Join(", ", step.FilteringAttributes.Select(Literal))} }}");
		if (key is not null) args.Add($"Key = {Literal(key)}");
		if (step.Name != DeclaredStep.DefaultName(typeName, step.Message, step.Entity, key)) args.Add($"Name = {Literal(step.Name)}");
		args.Add($"Id = {Literal(step.Id.ToString())}");

		return $"[PluginStep({string.Join(", ", args)})]";
	}

	private static string ImageAttribute(CurrentStep step, CurrentImage image, string? key)
	{
		var args = new List<string> { MessageArgument(step.Message), $"ImageType.{image.Type}" };
		if (image.Attributes is { Count: > 0 }) args.AddRange(image.Attributes.Select(Literal));
		if (!string.Equals(image.Alias, DeclarationBinder.DefaultAlias(image.Type), StringComparison.Ordinal)) args.Add($"Alias = {Literal(image.Alias)}");
		if (key is not null) args.Add($"Step = {Literal(key)}");

		return $"[PluginImage({string.Join(", ", args)})]";
	}

	private static string MessageArgument(string message) =>
		MessageConstants.Contains(message) ? $"Message.{message}" : Literal(message);

	private static string Literal(string value) => $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
}
