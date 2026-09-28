using Ten99.D365.PluginRegistration.Tool.Model;
using Ten99.D365.PluginRegistration.Tool.Planning;

namespace Ten99.D365.PluginRegistration.Tool.Tests;

public class PlannerTests
{
	private static readonly byte[] Content = [1, 2, 3];
	private static readonly Guid TypeId = Guid.NewGuid();
	private const string TypeName = "Plugins.ContactPlugin";

	private static DeclaredStep Step(string message = "Update", Stage stage = Stage.PostOperation, string[]? filtering = null,
		Guid? id = null, string? key = null, params DeclaredImage[] images) =>
		new(TypeName, message, "contact", stage, Mode.Synchronous, 1, filtering, id, key, images);

	private static DeclaredAssembly Assembly(params DeclaredStep[] steps) =>
		new("Plugins", "1.0.0.0", "neutral", "abc", Content, [new DeclaredType(TypeName, steps)]);

	private static CurrentStep Registered(DeclaredStep step, Guid? id = null, params CurrentImage[] images) =>
		new(id ?? Guid.NewGuid(), TypeId, step.Name, step.Message, step.Entity, step.Stage, step.Mode, step.Order, step.FilteringAttributes, images);

	private static CurrentState State(params CurrentStep[] steps) =>
		new(new CurrentAssembly(Guid.NewGuid(), "Plugins", "1.0.0.0", Content), [new CurrentType(TypeId, TypeName)], steps);

	[Fact]
	public void Creates_everything_in_an_empty_environment()
	{
		var plan = Planner.Create(Assembly(Step(images: new DeclaredImage(ImageType.PreImage, "preImage", ["name"]))), CurrentState.Empty, prune: false);

		Assert.Equal(
			[typeof(AssemblyChange), typeof(TypeChange), typeof(StepChange), typeof(ImageChange)],
			plan.Changes.Select(c => c.GetType()));
		Assert.All(plan.Changes, c => Assert.Equal(ChangeKind.Create, c.Kind));
	}

	[Fact]
	public void Has_no_changes_when_the_environment_matches()
	{
		var image = new DeclaredImage(ImageType.PreImage, "preImage", ["name"]);
		var step = Step(filtering: ["name"], images: image);
		var current = State(Registered(step, null, new CurrentImage(Guid.NewGuid(), ImageType.PreImage, "preImage", "preImage", ["name"])));

		var plan = Planner.Create(Assembly(step), current, prune: false);

		Assert.False(plan.HasChanges);
		Assert.True(plan.IsValid);
		Assert.Empty(plan.Warnings);
	}

	[Fact]
	public void Updates_the_assembly_when_its_content_changes()
	{
		var step = Step();
		var current = State(Registered(step)) with { Assembly = new CurrentAssembly(Guid.NewGuid(), "Plugins", "1.0.0.0", [9]) };

		var change = Assert.Single(Planner.Create(Assembly(step), current, prune: false).Changes);

		Assert.Equal(ChangeKind.Update, change.Kind);
		Assert.Contains("content changed", change.Details);
	}

	[Fact]
	public void Updates_a_step_matched_by_signature()
	{
		var registered = Registered(Step(filtering: ["name"]));
		var declared = Step(filtering: ["name", "email"]);

		var change = Assert.IsType<StepChange>(Assert.Single(Planner.Create(Assembly(declared), State(registered), prune: false).Changes));

		Assert.Equal(ChangeKind.Update, change.Kind);
		Assert.Same(registered, change.Current);
		Assert.Contains(change.Details, d => d.StartsWith("filtering attributes:"));
	}

	[Fact]
	public void Treats_filtering_attributes_as_a_set()
	{
		var registered = Registered(Step(filtering: ["b", "a"]));

		Assert.False(Planner.Create(Assembly(Step(filtering: ["a", "b"])), State(registered), prune: false).HasChanges);
	}

	[Fact]
	public void Adopts_an_existing_step_by_id_even_when_its_signature_changed()
	{
		var id = Guid.NewGuid();
		var registered = Registered(Step(stage: Stage.PreOperation), id);
		var declared = Step(id: id);

		var change = Assert.IsType<StepChange>(Assert.Single(Planner.Create(Assembly(declared), State(registered), prune: false).Changes));

		Assert.Equal(ChangeKind.Update, change.Kind);
		Assert.Contains("stage: PreOperation -> PostOperation", change.Details);
	}

	[Fact]
	public void Creates_a_pinned_step_with_its_id()
	{
		var id = Guid.NewGuid();

		var change = Assert.IsType<StepChange>(Assert.Single(Planner.Create(Assembly(Step(id: id)), State(), prune: false).Changes));

		Assert.Equal(ChangeKind.Create, change.Kind);
		Assert.Equal(id, change.Declared!.Id);
	}

	[Fact]
	public void Matches_keyed_steps_by_name()
	{
		var a = Step(key: "a");
		var b = Step(key: "b");
		var current = State(Registered(b), Registered(a));

		Assert.False(Planner.Create(Assembly(a, b), current, prune: false).HasChanges);
	}

	[Fact]
	public void Warns_about_undeclared_steps_unless_pruning()
	{
		var stray = Registered(Step(message: "Delete", stage: Stage.PreOperation));

		var kept = Planner.Create(Assembly(), State(stray), prune: false);
		Assert.False(kept.HasChanges);
		Assert.Single(kept.Warnings);

		var pruned = Planner.Create(Assembly(), State(stray), prune: true);
		var change = Assert.IsType<StepChange>(Assert.Single(pruned.Changes));
		Assert.Equal(ChangeKind.Delete, change.Kind);
	}

	[Fact]
	public void Errors_on_a_removed_type_unless_pruning()
	{
		var orphan = new CurrentType(Guid.NewGuid(), "Plugins.Removed");
		var state = State() with { Types = [new CurrentType(TypeId, TypeName), orphan] };

		Assert.False(Planner.Create(Assembly(), state, prune: false).IsValid);

		var pruned = Planner.Create(Assembly(), state, prune: true);
		Assert.True(pruned.IsValid);
		Assert.Contains(pruned.Changes, c => c is TypeChange { Kind: ChangeKind.Delete });
	}

	[Fact]
	public void Adds_updates_and_removes_images_on_a_declared_step()
	{
		var step = Step(images:
		[
			new DeclaredImage(ImageType.PreImage, "preImage", ["name", "email"]),
			new DeclaredImage(ImageType.PostImage, "postImage", null),
		]);
		var current = State(Registered(step, null,
			new CurrentImage(Guid.NewGuid(), ImageType.PreImage, "preImage", "preImage", ["name"]),
			new CurrentImage(Guid.NewGuid(), ImageType.PreImage, "old", "old", null)));

		var changes = Planner.Create(Assembly(step), current, prune: false).Changes.OfType<ImageChange>().ToList();

		Assert.Contains(changes, c => c.Kind == ChangeKind.Update && c.Declared!.Alias == "preImage");
		Assert.Contains(changes, c => c.Kind == ChangeKind.Create && c.Declared!.Type == ImageType.PostImage);
		Assert.Contains(changes, c => c.Kind == ChangeKind.Delete && c.Current!.Alias == "old");
	}

	[Fact]
	public void Errors_when_an_id_belongs_to_another_type()
	{
		var id = Guid.NewGuid();
		var elsewhere = Registered(Step(), id) with { PluginTypeId = Guid.NewGuid() };

		Assert.False(Planner.Create(Assembly(Step(id: id)), State(elsewhere), prune: false).IsValid);
	}
}
