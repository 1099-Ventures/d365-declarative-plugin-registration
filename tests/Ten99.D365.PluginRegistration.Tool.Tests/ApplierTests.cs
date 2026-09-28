using Ten99.D365.PluginRegistration.Tool.Applying;
using Ten99.D365.PluginRegistration.Tool.Dataverse;
using Ten99.D365.PluginRegistration.Tool.Model;
using Ten99.D365.PluginRegistration.Tool.Planning;

namespace Ten99.D365.PluginRegistration.Tool.Tests;

public class ApplierTests
{
	private const string TypeName = "Plugins.ContactPlugin";
	private static readonly byte[] Content = [1, 2, 3];

	private static DeclaredStep Step(string message = "Update", Guid? id = null, params DeclaredImage[] images) =>
		new(TypeName, message, "contact", Stage.PostOperation, Mode.Synchronous, 1, null, id, null, images);

	private static DeclaredAssembly Assembly(params DeclaredType[] types) => new("Plugins", "1.0.0.0", "neutral", "abc", Content, types);

	private static async Task<RecordingTarget> Apply(DeclaredAssembly declared, CurrentState current, bool prune = false, string? solution = null)
	{
		var target = new RecordingTarget();
		var plan = Planner.Create(declared, current, prune);
		await new Applier(target, TextWriter.Null).ApplyAsync(plan, current, solution, CancellationToken.None);
		return target;
	}

	[Fact]
	public async Task Creates_in_dependency_order_and_passes_new_ids_down()
	{
		var step = Step(images: new DeclaredImage(ImageType.PostImage, "postImage", null));

		var target = await Apply(Assembly(new DeclaredType(TypeName, [step])), CurrentState.Empty, solution: "CEI Plugins");

		Assert.Equal(
			[
				"resolve solution CEI Plugins",
				"create assembly Plugins",
				$"create type {TypeName} in {target.Id("assembly")}",
				$"create step {step.Name} on {target.Id("type")}",
				$"create image postImage on {target.Id("step")}",
				$"add assembly {target.Id("assembly")} to cei_plugins",
				$"add step {target.Id("step")} to cei_plugins",
			],
			target.Calls);
	}

	[Fact]
	public async Task Removes_a_deleted_type_before_updating_the_assembly()
	{
		var keptType = new CurrentType(Guid.NewGuid(), TypeName);
		var removedType = new CurrentType(Guid.NewGuid(), "Plugins.Removed");
		var removedStep = new CurrentStep(Guid.NewGuid(), removedType.Id, "old", "Update", "contact", Stage.PostOperation, Mode.Synchronous, 1, null, []);
		var current = new CurrentState(new CurrentAssembly(Guid.NewGuid(), "Plugins", "0.9.0.0", [9]), [keptType, removedType], [removedStep]);

		var target = await Apply(Assembly(new DeclaredType(TypeName, [])), current, prune: true);

		Assert.Equal(
			[
				$"delete step {removedStep.Id}",
				$"delete type {removedType.Id}",
				$"update assembly {current.Assembly!.Id}",
			],
			target.Calls);
	}

	[Fact]
	public async Task Adds_existing_unchanged_steps_to_the_solution()
	{
		var step = Step();
		var type = new CurrentType(Guid.NewGuid(), TypeName);
		var existing = new CurrentStep(Guid.NewGuid(), type.Id, step.Name, "Update", "contact", Stage.PostOperation, Mode.Synchronous, 1, null, []);
		var current = new CurrentState(new CurrentAssembly(Guid.NewGuid(), "Plugins", "1.0.0.0", Content), [type], [existing]);

		var target = await Apply(Assembly(new DeclaredType(TypeName, [step])), current, solution: "cei_plugins");

		Assert.Contains($"add step {existing.Id} to cei_plugins", target.Calls);
		Assert.DoesNotContain(target.Calls, c => c.StartsWith("create") || c.StartsWith("update"));
	}

	[Fact]
	public async Task Refuses_an_invalid_plan()
	{
		var plan = new Plan(Assembly(), [], [], ["broken"]);

		await Assert.ThrowsAsync<InvalidOperationException>(() =>
			new Applier(new RecordingTarget(), TextWriter.Null).ApplyAsync(plan, CurrentState.Empty, null, CancellationToken.None));
	}

	[Theory]
	[InlineData("Create", "Id")]
	[InlineData("Update", "Target")]
	[InlineData("SetState", "EntityMoniker")]
	[InlineData("Delete", "Target")]
	public void Uses_the_right_message_property_for_images(string message, string expected)
	{
		Assert.Equal(expected, DataverseRegistrationTarget.MessagePropertyName(message));
	}

	private sealed class RecordingTarget : IRegistrationTarget
	{
		private readonly Dictionary<string, Guid> _ids = [];

		public List<string> Calls { get; } = [];

		public Guid Id(string kind) => _ids[kind];

		private Guid New(string kind) => _ids[kind] = Guid.NewGuid();

		private Task Record(string call)
		{
			Calls.Add(call);
			return Task.CompletedTask;
		}

		public Task<Guid> CreateAssemblyAsync(DeclaredAssembly assembly, CancellationToken cancellationToken)
		{
			Calls.Add($"create assembly {assembly.Name}");
			return Task.FromResult(New("assembly"));
		}

		public Task UpdateAssemblyAsync(Guid id, DeclaredAssembly assembly, CancellationToken cancellationToken) => Record($"update assembly {id}");

		public Task<Guid> CreateTypeAsync(Guid assemblyId, string typeName, CancellationToken cancellationToken)
		{
			Calls.Add($"create type {typeName} in {assemblyId}");
			return Task.FromResult(New("type"));
		}

		public Task DeleteTypeAsync(Guid id, CancellationToken cancellationToken) => Record($"delete type {id}");

		public Task<Guid> CreateStepAsync(Guid typeId, DeclaredStep step, CancellationToken cancellationToken)
		{
			Calls.Add($"create step {step.Name} on {typeId}");
			return Task.FromResult(New("step"));
		}

		public Task UpdateStepAsync(Guid id, Guid typeId, DeclaredStep step, CancellationToken cancellationToken) => Record($"update step {id}");

		public Task DeleteStepAsync(Guid id, CancellationToken cancellationToken) => Record($"delete step {id}");

		public Task CreateImageAsync(Guid stepId, DeclaredStep step, DeclaredImage image, CancellationToken cancellationToken) =>
			Record($"create image {image.Alias} on {stepId}");

		public Task UpdateImageAsync(Guid id, DeclaredStep step, DeclaredImage image, CancellationToken cancellationToken) => Record($"update image {id}");

		public Task DeleteImageAsync(Guid id, CancellationToken cancellationToken) => Record($"delete image {id}");

		public Task<string> ResolveSolutionAsync(string solution, CancellationToken cancellationToken)
		{
			Calls.Add($"resolve solution {solution}");
			return Task.FromResult("cei_plugins");
		}

		public Task AddAssemblyToSolutionAsync(Guid assemblyId, string solutionUniqueName, CancellationToken cancellationToken) =>
			Record($"add assembly {assemblyId} to {solutionUniqueName}");

		public Task AddStepToSolutionAsync(Guid stepId, string solutionUniqueName, CancellationToken cancellationToken) =>
			Record($"add step {stepId} to {solutionUniqueName}");
	}
}
