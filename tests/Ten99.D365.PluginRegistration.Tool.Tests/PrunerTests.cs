using Ten99.D365.PluginRegistration.Tool.Applying;
using Ten99.D365.PluginRegistration.Tool.Model;

namespace Ten99.D365.PluginRegistration.Tool.Tests;

public class PrunerTests
{
	private const string TypeName = "Plugins.AccountPlugin";
	private static readonly Guid TypeId = Guid.NewGuid();

	private static readonly DeclaredStep Declared =
		new(TypeName, "Update", "account", Stage.PostOperation, Mode.Asynchronous, 1, ["statecode"], null, null, []);

	private static CurrentStep Registered(bool managed = false) =>
		new(Guid.NewGuid(), TypeId, Declared.Name, "Update", "account", Stage.PostOperation, Mode.Asynchronous, 1, ["statecode"], [], managed);

	private static (DeclaredAssembly, CurrentState) Setup(params CurrentStep[] steps) =>
	(
		new DeclaredAssembly("Plugins", "2.0.0.0", "neutral", "abc", [1], [new DeclaredType(TypeName, [Declared])]),
		new CurrentState(new CurrentAssembly(Guid.NewGuid(), "Plugins", "1.0.0.0", [9]), [new CurrentType(TypeId, TypeName)], steps)
	);

	[Fact]
	public void Selects_only_the_undeclared_duplicate()
	{
		var kept = Registered();
		var duplicate = Registered();
		var (declared, current) = Setup(kept, duplicate);

		var (deletable, managed) = Pruner.Select(declared, current);

		Assert.Single(deletable);
		Assert.Empty(managed);
		Assert.Contains(deletable[0].Id, new[] { kept.Id, duplicate.Id });
	}

	[Fact]
	public void Holds_back_managed_steps()
	{
		var (declared, current) = Setup(Registered(), Registered(managed: true), Registered(managed: true));

		var (deletable, managed) = Pruner.Select(declared, current);

		//	One of the three matches the declaration. The two undeclared are managed or not, depending on the match.
		Assert.Equal(2, deletable.Count + managed.Count);
		Assert.All(managed, s => Assert.True(s.IsManaged));
		Assert.All(deletable, s => Assert.False(s.IsManaged));
	}

	[Fact]
	public void Has_nothing_to_prune_when_everything_is_declared()
	{
		var (declared, current) = Setup(Registered());

		Assert.Empty(Pruner.Select(declared, current).Deletable);
	}
}
