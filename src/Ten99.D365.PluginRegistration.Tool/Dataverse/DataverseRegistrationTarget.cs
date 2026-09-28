using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Ten99.D365.PluginRegistration.Tool.Applying;
using Ten99.D365.PluginRegistration.Tool.Model;

namespace Ten99.D365.PluginRegistration.Tool.Dataverse;

public sealed class DataverseRegistrationTarget(IOrganizationServiceAsync2 service) : IRegistrationTarget
{
	private const int IsolationModeSandbox = 2;
	private const int SourceTypeDatabase = 0;
	private const int DeploymentServerOnly = 0;
	private const int ComponentTypePluginAssembly = 91;
	private const int ComponentTypeStep = 92;

	private readonly Dictionary<string, Guid> _messages = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<(Guid, string), Guid> _filters = [];

	public async Task<Guid> CreateAssemblyAsync(DeclaredAssembly assembly, CancellationToken cancellationToken)
	{
		var entity = AssemblyEntity(assembly);
		entity["isolationmode"] = new OptionSetValue(IsolationModeSandbox);
		entity["sourcetype"] = new OptionSetValue(SourceTypeDatabase);
		return await service.CreateAsync(entity, cancellationToken);
	}

	public Task UpdateAssemblyAsync(Guid id, DeclaredAssembly assembly, CancellationToken cancellationToken)
	{
		var entity = AssemblyEntity(assembly);
		entity.Id = id;
		return service.UpdateAsync(entity, cancellationToken);
	}

	public Task<Guid> CreateTypeAsync(Guid assemblyId, string typeName, CancellationToken cancellationToken) =>
		service.CreateAsync(new Entity("plugintype")
		{
			["pluginassemblyid"] = new EntityReference("pluginassembly", assemblyId),
			["typename"] = typeName,
			["name"] = typeName,
			["friendlyname"] = typeName,
		}, cancellationToken);

	public Task DeleteTypeAsync(Guid id, CancellationToken cancellationToken) =>
		service.DeleteAsync("plugintype", id, cancellationToken);

	public async Task<Guid> CreateStepAsync(Guid typeId, DeclaredStep step, CancellationToken cancellationToken)
	{
		var entity = await StepEntityAsync(typeId, step, cancellationToken);
		if (step.Id is Guid id) entity.Id = id;
		entity["supporteddeployment"] = new OptionSetValue(DeploymentServerOnly);
		return await service.CreateAsync(entity, cancellationToken);
	}

	public async Task UpdateStepAsync(Guid id, Guid typeId, DeclaredStep step, CancellationToken cancellationToken)
	{
		var entity = await StepEntityAsync(typeId, step, cancellationToken);
		entity.Id = id;
		await service.UpdateAsync(entity, cancellationToken);
	}

	public Task DeleteStepAsync(Guid id, CancellationToken cancellationToken) =>
		service.DeleteAsync("sdkmessageprocessingstep", id, cancellationToken);

	public Task CreateImageAsync(Guid stepId, DeclaredStep step, DeclaredImage image, CancellationToken cancellationToken)
	{
		var entity = ImageEntity(step, image);
		entity["sdkmessageprocessingstepid"] = new EntityReference("sdkmessageprocessingstep", stepId);
		return service.CreateAsync(entity, cancellationToken);
	}

	public Task UpdateImageAsync(Guid id, DeclaredStep step, DeclaredImage image, CancellationToken cancellationToken)
	{
		//	Leave an existing image's name alone. Only the alias matters to the plugin.
		var entity = ImageEntity(step, image);
		entity.Attributes.Remove("name");
		entity.Id = id;
		return service.UpdateAsync(entity, cancellationToken);
	}

	public Task DeleteImageAsync(Guid id, CancellationToken cancellationToken) =>
		service.DeleteAsync("sdkmessageprocessingstepimage", id, cancellationToken);

	public async Task<string> ResolveSolutionAsync(string solution, CancellationToken cancellationToken)
	{
		var query = new QueryExpression("solution")
		{
			ColumnSet = new ColumnSet("uniquename", "friendlyname", "ismanaged"),
			Criteria =
			{
				FilterOperator = LogicalOperator.Or,
				Conditions =
				{
					new ConditionExpression("uniquename", ConditionOperator.Equal, solution),
					new ConditionExpression("friendlyname", ConditionOperator.Equal, solution),
				},
			},
		};

		var matches = (await service.RetrieveMultipleAsync(query, cancellationToken)).Entities;
		var match = matches.FirstOrDefault(e => string.Equals(e.GetAttributeValue<string>("uniquename"), solution, StringComparison.OrdinalIgnoreCase))
			?? (matches.Count == 1 ? matches[0] : null)
			?? throw new InvalidOperationException(matches.Count == 0
				? $"Solution '{solution}' was not found."
				: $"More than one solution is called '{solution}'. Use its unique name.");

		if (match.GetAttributeValue<bool>("ismanaged"))
		{
			throw new InvalidOperationException($"Solution '{solution}' is managed. Components can only be added to an unmanaged solution.");
		}

		return match.GetAttributeValue<string>("uniquename");
	}

	public Task AddAssemblyToSolutionAsync(Guid assemblyId, string solutionUniqueName, CancellationToken cancellationToken) =>
		AddToSolutionAsync(ComponentTypePluginAssembly, assemblyId, solutionUniqueName, cancellationToken);

	public Task AddStepToSolutionAsync(Guid stepId, string solutionUniqueName, CancellationToken cancellationToken) =>
		AddToSolutionAsync(ComponentTypeStep, stepId, solutionUniqueName, cancellationToken);

	private Task AddToSolutionAsync(int componentType, Guid id, string solutionUniqueName, CancellationToken cancellationToken) =>
		service.ExecuteAsync(new AddSolutionComponentRequest
		{
			ComponentType = componentType,
			ComponentId = id,
			SolutionUniqueName = solutionUniqueName,
			AddRequiredComponents = false,
		}, cancellationToken);

	private static Entity AssemblyEntity(DeclaredAssembly assembly) => new("pluginassembly")
	{
		["name"] = assembly.Name,
		["content"] = Convert.ToBase64String(assembly.Content),
		["version"] = assembly.Version,
		["culture"] = assembly.Culture,
		["publickeytoken"] = assembly.PublicKeyToken,
	};

	private async Task<Entity> StepEntityAsync(Guid typeId, DeclaredStep step, CancellationToken cancellationToken)
	{
		var messageId = await ResolveMessageAsync(step.Message, cancellationToken);

		return new Entity("sdkmessageprocessingstep")
		{
			["name"] = step.Name,
			["eventhandler"] = new EntityReference("plugintype", typeId),
			["sdkmessageid"] = new EntityReference("sdkmessage", messageId),
			["sdkmessagefilterid"] = step.Entity is null
				? null
				: new EntityReference("sdkmessagefilter", await ResolveFilterAsync(messageId, step, cancellationToken)),
			["stage"] = new OptionSetValue((int)step.Stage),
			["mode"] = new OptionSetValue((int)step.Mode),
			["rank"] = step.Order,
			["filteringattributes"] = step.FilteringAttributes is null ? null : string.Join(",", step.FilteringAttributes),
		};
	}

	private static Entity ImageEntity(DeclaredStep step, DeclaredImage image) => new("sdkmessageprocessingstepimage")
	{
		["imagetype"] = new OptionSetValue((int)image.Type),
		["entityalias"] = image.Alias,
		["name"] = image.Name,
		["attributes"] = image.Attributes is null ? null : string.Join(",", image.Attributes),
		["messagepropertyname"] = MessagePropertyName(step.Message),
	};

	/// <summary>The request property that holds the record an image is taken of.</summary>
	internal static string MessagePropertyName(string message) => message.ToLowerInvariant() switch
	{
		"create" => "Id",
		"setstate" or "setstatedynamicentity" => "EntityMoniker",
		_ => "Target",
	};

	private async Task<Guid> ResolveMessageAsync(string message, CancellationToken cancellationToken)
	{
		if (_messages.TryGetValue(message, out var cached)) return cached;

		var query = new QueryExpression("sdkmessage")
		{
			ColumnSet = new ColumnSet("sdkmessageid"),
			Criteria = { Conditions = { new ConditionExpression("name", ConditionOperator.Equal, message) } },
		};

		var entity = (await service.RetrieveMultipleAsync(query, cancellationToken)).Entities.FirstOrDefault()
			?? throw new InvalidOperationException($"Message '{message}' does not exist in this environment.");

		return _messages[message] = entity.Id;
	}

	private async Task<Guid> ResolveFilterAsync(Guid messageId, DeclaredStep step, CancellationToken cancellationToken)
	{
		var key = (messageId, step.Entity!);
		if (_filters.TryGetValue(key, out var cached)) return cached;

		var query = new QueryExpression("sdkmessagefilter")
		{
			ColumnSet = new ColumnSet("sdkmessagefilterid"),
			Criteria =
			{
				Conditions =
				{
					new ConditionExpression("sdkmessageid", ConditionOperator.Equal, messageId),
					new ConditionExpression("primaryobjecttypecode", ConditionOperator.Equal, step.Entity),
				},
			},
		};

		var entity = (await service.RetrieveMultipleAsync(query, cancellationToken)).Entities.FirstOrDefault()
			?? throw new InvalidOperationException($"{step.Name}: '{step.Message}' can't be registered on '{step.Entity}'.");

		return _filters[key] = entity.Id;
	}
}
