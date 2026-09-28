namespace Ten99.D365.PluginRegistration.Tool.Model;

// Mirrors the enums in the attribute package. Values match the Dataverse option sets.
public enum Stage
{
	PreValidation = 10,
	PreOperation = 20,
	PostOperation = 40,
}

public enum Mode
{
	Synchronous = 0,
	Asynchronous = 1,
}

public enum ImageType
{
	PreImage = 0,
	PostImage = 1,
	Both = 2,
}

/// <summary>A plugin assembly and every plugin type it contains, with their declared registrations.</summary>
public sealed record DeclaredAssembly(
	string Name,
	string Version,
	string Culture,
	string PublicKeyToken,
	byte[] Content,
	IReadOnlyList<DeclaredType> Types);

public sealed record DeclaredType(string TypeName, IReadOnlyList<DeclaredStep> Steps);

public sealed record DeclaredStep(
	string TypeName,
	string Message,
	string? Entity,
	Stage Stage,
	Mode Mode,
	int Order,
	IReadOnlyList<string>? FilteringAttributes,
	Guid? Id,
	string? Key,
	IReadOnlyList<DeclaredImage> Images)
{
	/// <summary>Plugin Registration Tool style name, so adopted steps keep familiar names.</summary>
	public string Name => $"{TypeName}: {Message} of {Entity ?? "any Entity"}{(Key is null ? "" : $" ({Key})")}";

	public string Describe() => $"{Name} [{Stage}, {Mode}]";
}

public sealed record DeclaredImage(ImageType Type, string Alias, IReadOnlyList<string>? Attributes)
{
	public string Name => Alias;
}

/// <summary>A step attribute as read from metadata, before images are bound to it.</summary>
public sealed record RawStep(
	string Message,
	string? Entity,
	Stage Stage,
	Mode Mode,
	int Order,
	IReadOnlyList<string>? FilteringAttributes,
	string? Id,
	string? Key);

/// <summary>An image attribute as read from metadata.</summary>
public sealed record RawImage(string Message, ImageType Type, IReadOnlyList<string>? Attributes, string? Alias, string? Step);
