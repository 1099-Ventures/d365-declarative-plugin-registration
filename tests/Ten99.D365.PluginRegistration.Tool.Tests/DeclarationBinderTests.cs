using Ten99.D365.PluginRegistration.Tool.Model;
using Ten99.D365.PluginRegistration.Tool.Reading;

namespace Ten99.D365.PluginRegistration.Tool.Tests;

public class DeclarationBinderTests
{
	private static RawStep Step(string message, string? entity = "contact", Stage stage = Stage.PostOperation, Mode mode = Mode.Synchronous,
		string[]? filtering = null, string? id = null, string? key = null) =>
		new(message, entity, stage, mode, 1, filtering, id, key);

	private static RawImage Image(string message, ImageType type, string? step = null, string? alias = null) =>
		new(message, type, null, alias, step);

	private static List<string> Errors(RawStep[] steps, params RawImage[] images)
	{
		var errors = new List<string>();
		DeclarationBinder.Bind("Plugin", steps, images, errors);
		return errors;
	}

	[Fact]
	public void Accepts_a_valid_declaration()
	{
		Assert.Empty(Errors([Step("Update", filtering: ["name"])], Image("Update", ImageType.PreImage), Image("Update", ImageType.PostImage)));
	}

	[Fact]
	public void Rejects_an_image_without_a_step_for_its_message()
	{
		Assert.Contains(Errors([Step("Create")], Image("Update", ImageType.PreImage)), e => e.Contains("has no Update step"));
	}

	[Fact]
	public void Rejects_an_ambiguous_image()
	{
		var errors = Errors([Step("Update", stage: Stage.PreOperation, key: "a"), Step("Update", key: "b")], Image("Update", ImageType.PreImage));

		Assert.Contains(errors, e => e.Contains("ambiguous"));
	}

	[Fact]
	public void Rejects_an_unknown_step_key()
	{
		Assert.Contains(Errors([Step("Update", key: "a")], Image("Update", ImageType.PreImage, step: "b")), e => e.Contains("no step has that Key"));
	}

	[Theory]
	[InlineData("Create", ImageType.PreImage, Stage.PostOperation, "Create has no pre-image")]
	[InlineData("Delete", ImageType.PostImage, Stage.PostOperation, "Delete has no post-image")]
	[InlineData("Update", ImageType.PostImage, Stage.PreOperation, "only available on a PostOperation step")]
	[InlineData("Update", ImageType.Both, Stage.PreValidation, "only available on a PostOperation step")]
	public void Rejects_images_the_platform_cannot_provide(string message, ImageType type, Stage stage, string expected)
	{
		Assert.Contains(Errors([Step(message, stage: stage)], Image(message, type)), e => e.Contains(expected));
	}

	[Fact]
	public void Rejects_filtering_attributes_outside_update()
	{
		Assert.Contains(Errors([Step("Create", filtering: ["name"])]), e => e.Contains("only apply to Update"));
	}

	[Fact]
	public void Rejects_an_asynchronous_step_before_the_operation()
	{
		Assert.Contains(Errors([Step("Update", stage: Stage.PreOperation, mode: Mode.Asynchronous)]), e => e.Contains("must be PostOperation"));
	}

	[Fact]
	public void Rejects_duplicate_steps_without_keys()
	{
		Assert.Contains(Errors([Step("Update"), Step("Update")]), e => e.Contains("Give each a Key"));
	}

	[Fact]
	public void Rejects_a_bad_id()
	{
		Assert.Contains(Errors([Step("Update", id: "not-a-guid")]), e => e.Contains("is not a GUID"));
	}

	[Fact]
	public void Rejects_duplicate_image_aliases_on_one_step()
	{
		Assert.Contains(Errors([Step("Update")], Image("Update", ImageType.PreImage, alias: "x"), Image("Update", ImageType.PostImage, alias: "x")),
			e => e.Contains("alias 'x' is used more than once"));
	}

	[Fact]
	public void Normalises_attribute_lists_to_sorted_lowercase_sets()
	{
		Assert.Equal(["a", "b"], DeclarationBinder.Normalise(["B", " a", "b"]));
		Assert.Null(DeclarationBinder.Normalise([]));
	}
}
