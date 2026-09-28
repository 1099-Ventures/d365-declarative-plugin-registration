using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Ten99.D365.PluginRegistration.Tool.Exporting;
using Ten99.D365.PluginRegistration.Tool.Model;
using Ten99.D365.PluginRegistration.Tool.Planning;
using Ten99.D365.PluginRegistration.Tool.Reading;

namespace Ten99.D365.PluginRegistration.Tool.Tests;

public class ExporterTests
{
	private static readonly Uri Environment = new("https://org.crm.dynamics.com/");
	private static readonly Guid SingleId = Guid.NewGuid();
	private static readonly Guid MultiId = Guid.NewGuid();
	private static readonly Guid BareId = Guid.NewGuid();

	/// <summary>Hand-registered steps with the untidy details real environments have.</summary>
	private static CurrentState Registered()
	{
		var single = new CurrentType(SingleId, "Plugins.Single");
		var multi = new CurrentType(MultiId, "Plugins.Multi");
		var bare = new CurrentType(BareId, "Plugins.Bare");

		return new CurrentState(
			new CurrentAssembly(Guid.NewGuid(), "Plugins", "1.0.0.0", null),
			[single, multi, bare],
			[
				new(Guid.NewGuid(), SingleId, "Plugins.Single: Create of account", "Create", "account", Stage.PreOperation, Mode.Synchronous, 1, null, []),

				new(Guid.NewGuid(), MultiId, "Contact validation \"strict\"", "Update", "contact", Stage.PreOperation, Mode.Synchronous, 1, ["emailaddress1"],
					[new(Guid.NewGuid(), ImageType.PreImage, "PreImage", "Image", ["emailaddress1"])]),
				new(Guid.NewGuid(), MultiId, "Plugins.Multi: Update of contact", "Update", "contact", Stage.PostOperation, Mode.Asynchronous, 5, null,
					[new(Guid.NewGuid(), ImageType.PostImage, "postImage", "postImage", null)]),
				new(Guid.NewGuid(), MultiId, "Plugins.Multi: Update of contact", "Update", "contact", Stage.PostOperation, Mode.Asynchronous, 1, null, []),
				new(Guid.NewGuid(), MultiId, "Plugins.Multi: Associate of any Entity", "Associate", null, Stage.PostOperation, Mode.Synchronous, 1, null, []),
				new(Guid.NewGuid(), MultiId, "Custom message step", "PostCreate", "contact", Stage.PostOperation, Mode.Synchronous, 1, null, []),
			]);
	}

	[Fact]
	public void Writes_attributes_with_ids_keys_and_only_non_default_values()
	{
		var text = Exporter.Export(Registered(), Environment);

		Assert.Contains("// Plugins.Bare: no steps registered", text);
		Assert.Contains("[PluginStep(Message.Create, \"account\", Stage.PreOperation, Id = ", text);
		Assert.Contains("Key = \"preoperation\", Name = \"Contact validation \\\"strict\\\"\"", text);
		Assert.Contains("Key = \"postoperation1\"", text);
		Assert.Contains("[PluginStep(Message.Associate, Stage.PostOperation, Id = ", text);
		Assert.Contains("[PluginStep(\"PostCreate\", \"contact\"", text);
		Assert.Contains("[PluginImage(Message.Update, ImageType.PreImage, \"emailaddress1\", Alias = \"PreImage\", Step = \"preoperation\")]", text);
	}

	[Fact]
	public void Pasted_export_plans_no_changes()
	{
		var registered = Registered();
		var exported = Exporter.Export(registered, Environment);

		//	Paste each exported block onto its class, compile it like a plugin project, and read it back
		var source = new System.Text.StringBuilder("using System; using Microsoft.Xrm.Sdk; using Ten99.D365.PluginRegistration;\nnamespace Plugins {\n");
		foreach (var block in exported.Split("\n// ", StringSplitOptions.RemoveEmptyEntries).Where(b => b.StartsWith("Plugins.")))
		{
			var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries);
			var className = lines[0].Split(':')[0].Trim().Replace("Plugins.", "");
			source.AppendLine(string.Join("\n", lines.Skip(1)));
			source.AppendLine($"public class {className} : IPlugin {{ public void Execute(IServiceProvider serviceProvider) {{ }} }}");
		}

		source.AppendLine("}");
		var path = Compile(source.ToString());

		var errors = new List<string>();
		var declared = AssemblyReader.Read(path, errors);
		Assert.Empty(errors);

		//	Only the registrations are under test, not the assembly binary
		var assembly = registered.Assembly! with { Content = [], Version = declared.Version };
		var plan = Planner.Create(declared with { Content = [] }, registered with { Assembly = assembly }, prune: false);

		var printed = new StringWriter();
		PlanPrinter.Print(plan, Environment, printed);
		Assert.True(plan.IsValid && plan.Warnings.Count == 0 && plan.Changes.Count == 0, printed + "\n" + exported);
	}

	private static string Compile(string source)
	{
		string Metadata(string key) => typeof(ExporterTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == key).Value!;

		var fixtureDirectory = Path.GetDirectoryName(Metadata("FixtureAssembly"))!;
		var references = Directory.GetFiles(Metadata("Net462ReferenceAssemblies"), "*.dll")
			.Concat(Directory.GetFiles(Path.Combine(Metadata("Net462ReferenceAssemblies"), "Facades"), "*.dll"))
			.Append(Path.Combine(fixtureDirectory, "Microsoft.Xrm.Sdk.dll"))
			.Where(IsManaged)
			.Select(p => MetadataReference.CreateFromFile(p));

		var compilation = CSharpCompilation.Create(
			"Plugins",
			[
				CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp7_3)),
				CSharpSyntaxTree.ParseText(File.ReadAllText(Metadata("AttributeSource")), new CSharpParseOptions(LanguageVersion.CSharp7_3)),
			],
			references,
			new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

		//	The reader resolves references from the assembly's folder, as it would in a plugin's bin
		var directory = Directory.CreateTempSubdirectory("pluginreg-").FullName;
		File.Copy(Path.Combine(fixtureDirectory, "Microsoft.Xrm.Sdk.dll"), Path.Combine(directory, "Microsoft.Xrm.Sdk.dll"));

		var path = Path.Combine(directory, "Plugins.dll");
		var result = compilation.Emit(path);
		Assert.True(result.Success, string.Join("\n", result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
		return path;
	}

	//	The reference pack includes native DLLs and a bare module
	private static bool IsManaged(string path)
	{
		using var reader = new System.Reflection.PortableExecutable.PEReader(File.OpenRead(path));
		return reader.HasMetadata && System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(reader).IsAssembly;
	}
}
