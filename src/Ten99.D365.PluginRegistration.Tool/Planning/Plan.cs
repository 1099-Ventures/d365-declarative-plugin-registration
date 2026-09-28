using Ten99.D365.PluginRegistration.Tool.Model;

namespace Ten99.D365.PluginRegistration.Tool.Planning;

public enum ChangeKind
{
	Create,
	Update,
	Delete,
}

public abstract record Change(ChangeKind Kind, IReadOnlyList<string> Details)
{
	public abstract string Describe();
}

public sealed record AssemblyChange(ChangeKind Kind, DeclaredAssembly Declared, CurrentAssembly? Current, IReadOnlyList<string> Details)
	: Change(Kind, Details)
{
	public override string Describe() => $"assembly {Declared.Name} {Declared.Version}";
}

public sealed record TypeChange(ChangeKind Kind, string TypeName, CurrentType? Current)
	: Change(Kind, [])
{
	public override string Describe() => $"type {TypeName}";
}

/// <summary>Declared is null for a delete. Current is null for a create.</summary>
public sealed record StepChange(ChangeKind Kind, DeclaredStep? Declared, CurrentStep? Current, IReadOnlyList<string> Details)
	: Change(Kind, Details)
{
	public override string Describe() => $"step {Declared?.Describe() ?? $"{Current!.Name} [{Current.Stage}, {Current.Mode}]"}";
}

/// <summary>Step is the declared step the image belongs to. Declared is null for a delete.</summary>
public sealed record ImageChange(ChangeKind Kind, DeclaredStep Step, DeclaredImage? Declared, CurrentImage? Current, IReadOnlyList<string> Details)
	: Change(Kind, Details)
{
	public override string Describe() => $"{(Declared?.Type ?? Current!.Type)} '{Declared?.Alias ?? Current!.Alias}' on {Step.Name}";
}

public sealed record Plan(
	DeclaredAssembly Assembly,
	IReadOnlyList<Change> Changes,
	IReadOnlyList<string> Warnings,
	IReadOnlyList<string> Errors)
{
	public bool HasChanges => Changes.Count > 0;

	public bool IsValid => Errors.Count == 0;

	/// <summary>Steps that end up registered, with their ids once known. Used to add them to a solution.</summary>
	public IReadOnlyList<(DeclaredStep Step, CurrentStep? Current)> ManagedSteps { get; init; } = [];
}
