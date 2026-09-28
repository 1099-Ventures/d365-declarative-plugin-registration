using System.Reflection;
using Ten99.D365.PluginRegistration.Tool.Model;
using Ten99.D365.PluginRegistration.Tool.Reading;

namespace Ten99.D365.PluginRegistration.Tool.Tests;

public class AssemblyReaderTests
{
	private static readonly Lazy<(DeclaredAssembly Assembly, List<string> Errors)> Fixture = new(() =>
	{
		var path = typeof(AssemblyReaderTests).Assembly
			.GetCustomAttributes<AssemblyMetadataAttribute>()
			.Single(a => a.Key == "FixtureAssembly").Value!;

		var errors = new List<string>();
		return (AssemblyReader.Read(path, errors), errors);
	});

	private static DeclaredType Type(string name) => Fixture.Value.Assembly.Types.Single(t => t.TypeName == $"Fixtures.{name}");

	[Fact]
	public void Reads_without_errors()
	{
		Assert.Empty(Fixture.Value.Errors);
		Assert.Equal("Ten99.D365.PluginRegistration.Fixtures", Fixture.Value.Assembly.Name);
		Assert.NotEmpty(Fixture.Value.Assembly.Content);
	}

	[Fact]
	public void Includes_every_concrete_plugin_and_nothing_else()
	{
		var names = Fixture.Value.Assembly.Types.Select(t => t.TypeName).ToList();

		Assert.Equal(
			["Fixtures.EarlyBoundWithImage", "Fixtures.KeyedSteps", "Fixtures.LateBoundSingleStep", "Fixtures.NoEntity", "Fixtures.Undeclared"],
			names);
	}

	[Fact]
	public void Reads_a_late_bound_step_with_defaults()
	{
		var step = Assert.Single(Type("LateBoundSingleStep").Steps);

		Assert.Equal("Create", step.Message);
		Assert.Equal("knowledgearticle", step.Entity);
		Assert.Equal(Stage.PreOperation, step.Stage);
		Assert.Equal(Mode.Synchronous, step.Mode);
		Assert.Equal(1, step.Order);
		Assert.Null(step.FilteringAttributes);
		Assert.Empty(step.Images);
		Assert.Equal("Fixtures.LateBoundSingleStep: Create of knowledgearticle", step.Name);
	}

	[Fact]
	public void Resolves_the_entity_from_an_early_bound_type_and_binds_the_image()
	{
		var update = Type("EarlyBoundWithImage").Steps.Single(s => s.Message == "Update");

		Assert.Equal("lead", update.Entity);
		Assert.Equal(Mode.Asynchronous, update.Mode);
		Assert.Equal(5, update.Order);
		Assert.Equal(["firstname", "lastname"], update.FilteringAttributes);

		var image = Assert.Single(update.Images);
		Assert.Equal(ImageType.PostImage, image.Type);
		Assert.Equal("postImage", image.Alias);
		Assert.Equal(["firstname", "lastname"], image.Attributes);
	}

	[Fact]
	public void Binds_images_to_keyed_steps()
	{
		var steps = Type("KeyedSteps").Steps;
		var validate = steps.Single(s => s.Key == "validate");
		var sync = steps.Single(s => s.Key == "sync");

		Assert.Equal(Guid.Parse("5b7c2f1e-0d4a-4c3b-9e8f-2a6d1c0b9e71"), validate.Id);
		Assert.Equal("before", Assert.Single(validate.Images).Alias);

		var post = Assert.Single(sync.Images);
		Assert.Equal(ImageType.PostImage, post.Type);
		Assert.Null(post.Attributes);
		Assert.Equal("Fixtures.KeyedSteps: Update of contact (sync)", sync.Name);
	}

	[Fact]
	public void Reads_a_step_without_an_entity()
	{
		var step = Assert.Single(Type("NoEntity").Steps);

		Assert.Equal("Associate", step.Message);
		Assert.Null(step.Entity);
		Assert.Equal("Fixtures.NoEntity: Associate of any Entity", step.Name);
	}

	[Fact]
	public void Registers_a_plugin_without_steps_as_a_type_only()
	{
		Assert.Empty(Type("Undeclared").Steps);
	}
}
