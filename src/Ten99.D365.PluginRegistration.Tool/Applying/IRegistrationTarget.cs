using Ten99.D365.PluginRegistration.Tool.Model;

namespace Ten99.D365.PluginRegistration.Tool.Applying;

/// <summary>The writes apply needs. Implemented against Dataverse, and by a recording fake in tests.</summary>
public interface IRegistrationTarget
{
	Task<Guid> CreateAssemblyAsync(DeclaredAssembly assembly, CancellationToken cancellationToken);

	Task UpdateAssemblyAsync(Guid id, DeclaredAssembly assembly, CancellationToken cancellationToken);

	Task<Guid> CreateTypeAsync(Guid assemblyId, string typeName, CancellationToken cancellationToken);

	Task DeleteTypeAsync(Guid id, CancellationToken cancellationToken);

	/// <summary>Creates the step with its declared Id when it has one.</summary>
	Task<Guid> CreateStepAsync(Guid typeId, DeclaredStep step, CancellationToken cancellationToken);

	Task UpdateStepAsync(Guid id, Guid typeId, DeclaredStep step, CancellationToken cancellationToken);

	Task DeleteStepAsync(Guid id, CancellationToken cancellationToken);

	Task CreateImageAsync(Guid stepId, DeclaredStep step, DeclaredImage image, CancellationToken cancellationToken);

	Task UpdateImageAsync(Guid id, DeclaredStep step, DeclaredImage image, CancellationToken cancellationToken);

	Task DeleteImageAsync(Guid id, CancellationToken cancellationToken);

	/// <summary>Resolves a solution by unique name or display name, returning its unique name.</summary>
	Task<string> ResolveSolutionAsync(string solution, CancellationToken cancellationToken);

	Task AddAssemblyToSolutionAsync(Guid assemblyId, string solutionUniqueName, CancellationToken cancellationToken);

	Task AddStepToSolutionAsync(Guid stepId, string solutionUniqueName, CancellationToken cancellationToken);
}
