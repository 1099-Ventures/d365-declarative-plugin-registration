namespace Ten99.D365.PluginRegistration.Tool.Model;

/// <summary>What is registered in the environment for one plugin assembly.</summary>
public sealed record CurrentState(
	CurrentAssembly? Assembly,
	IReadOnlyList<CurrentType> Types,
	IReadOnlyList<CurrentStep> Steps)
{
	public static CurrentState Empty { get; } = new(null, [], []);
}

public sealed record CurrentAssembly(Guid Id, string Name, string Version, byte[]? Content);

public sealed record CurrentType(Guid Id, string TypeName);

public sealed record CurrentStep(
	Guid Id,
	Guid PluginTypeId,
	string Name,
	string Message,
	string? Entity,
	Stage Stage,
	Mode Mode,
	int Order,
	IReadOnlyList<string>? FilteringAttributes,
	IReadOnlyList<CurrentImage> Images,
	bool IsManaged = false);

public sealed record CurrentImage(Guid Id, ImageType Type, string Alias, string Name, IReadOnlyList<string>? Attributes);
