using System.Reflection;
using System.Runtime.InteropServices;
using Ten99.D365.PluginRegistration.Tool.Model;

namespace Ten99.D365.PluginRegistration.Tool.Reading;

/// <summary>
/// Reads a compiled plugin assembly without loading it, so a .NET Framework assembly can be
/// inspected from .NET on any OS. References are resolved from the assembly's own folder.
/// </summary>
public static class AssemblyReader
{
	private const string AttributeNamespace = "Ten99.D365.PluginRegistration";
	private const string StepAttribute = AttributeNamespace + ".PluginStepAttribute";
	private const string ImageAttribute = AttributeNamespace + ".PluginImageAttribute";
	private const string PluginInterface = "Microsoft.Xrm.Sdk.IPlugin";

	public static DeclaredAssembly Read(string path, ICollection<string> errors)
	{
		var fullPath = Path.GetFullPath(path);
		var directory = Path.GetDirectoryName(fullPath)!;

		//	The plugin's folder wins over the runtime for anything it ships itself (e.g. Microsoft.Xrm.Sdk)
		var candidates = Directory.GetFiles(directory, "*.dll")
			.Concat(Directory.GetFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll"))
			.GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
			.Select(g => g.First());

		using var context = new MetadataLoadContext(new PathAssemblyResolver(candidates));
		var assembly = context.LoadFromAssemblyPath(fullPath);
		var name = assembly.GetName();

		var types = new List<DeclaredType>();
		foreach (var type in GetLoadableTypes(assembly, errors).Where(IsPluginType).OrderBy(t => t.FullName, StringComparer.Ordinal))
		{
			var typeName = type.FullName!;
			var attributes = type.GetCustomAttributesData();

			var steps = attributes
				.Where(a => a.AttributeType.FullName == StepAttribute)
				.Select(a => ReadStep(typeName, a, errors))
				.OfType<RawStep>()
				.ToList();

			var images = attributes
				.Where(a => a.AttributeType.FullName == ImageAttribute)
				.Select(ReadImage)
				.ToList();

			types.Add(DeclarationBinder.Bind(typeName, steps, images, errors));
		}

		return new DeclaredAssembly(
			name.Name!,
			name.Version?.ToString() ?? "0.0.0.0",
			string.IsNullOrEmpty(name.CultureName) ? "neutral" : name.CultureName,
			FormatToken(name.GetPublicKeyToken()),
			File.ReadAllBytes(fullPath),
			types);
	}

	private static IEnumerable<Type> GetLoadableTypes(Assembly assembly, ICollection<string> errors)
	{
		try
		{
			return assembly.GetTypes();
		}
		catch (ReflectionTypeLoadException ex)
		{
			foreach (var loaderError in ex.LoaderExceptions.OfType<Exception>().Select(e => e.Message).Distinct())
			{
				errors.Add($"Could not load a type from {assembly.GetName().Name}: {loaderError}");
			}

			return ex.Types.OfType<Type>();
		}
	}

	private static bool IsPluginType(Type type) =>
		type is { IsClass: true, IsAbstract: false, IsPublic: true }
		&& type.GetInterfaces().Any(i => i.FullName == PluginInterface);

	private static RawStep? ReadStep(string typeName, CustomAttributeData data, ICollection<string> errors)
	{
		var args = data.ConstructorArguments;
		var message = (string)args[0].Value!;
		string? entity = null;
		Stage stage;

		if (args.Count == 2)
		{
			//	(message, stage): no primary entity
			stage = (Stage)(int)args[1].Value!;
		}
		else if (args[1].Value is Type entityType)
		{
			//	(message, typeof(Entity), stage): early-bound
			entity = ReadEntityLogicalName(entityType);
			if (entity is null)
			{
				errors.Add($"{typeName}: {entityType.FullName} has no EntityLogicalName constant.");
				return null;
			}

			stage = (Stage)(int)args[2].Value!;
		}
		else
		{
			//	(message, "entity", stage): late-bound
			entity = (string?)args[1].Value;
			stage = (Stage)(int)args[2].Value!;
		}

		var named = data.NamedArguments.ToDictionary(a => a.MemberName, a => a.TypedValue.Value, StringComparer.Ordinal);

		return new RawStep(
			message,
			string.IsNullOrWhiteSpace(entity) || string.Equals(entity, "none", StringComparison.OrdinalIgnoreCase) ? null : entity.ToLowerInvariant(),
			stage,
			named.TryGetValue("Mode", out var mode) ? (Mode)(int)mode! : Mode.Synchronous,
			named.TryGetValue("Order", out var order) ? (int)order! : 1,
			named.TryGetValue("FilteringAttributes", out var filtering) ? ReadStrings(filtering) : null,
			named.TryGetValue("Id", out var id) ? (string?)id : null,
			named.TryGetValue("Key", out var key) ? (string?)key : null,
			named.TryGetValue("Name", out var stepName) ? (string?)stepName : null);
	}

	private static RawImage ReadImage(CustomAttributeData data)
	{
		var args = data.ConstructorArguments;
		var named = data.NamedArguments.ToDictionary(a => a.MemberName, a => a.TypedValue.Value, StringComparer.Ordinal);

		return new RawImage(
			(string)args[0].Value!,
			(ImageType)(int)args[1].Value!,
			ReadStrings(args[2].Value),
			named.TryGetValue("Alias", out var alias) ? (string?)alias : null,
			named.TryGetValue("Step", out var step) ? (string?)step : null);
	}

	private static List<string>? ReadStrings(object? value) =>
		value is IReadOnlyCollection<CustomAttributeTypedArgument> items
			? items.Select(i => (string?)i.Value).OfType<string>().ToList()
			: null;

	private static string? ReadEntityLogicalName(Type entityType)
	{
		var field = entityType.GetField("EntityLogicalName", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
		return field is { IsLiteral: true } ? field.GetRawConstantValue() as string : null;
	}

	private static string FormatToken(byte[]? token) =>
		token is { Length: > 0 } ? Convert.ToHexString(token).ToLowerInvariant() : "null";
}
