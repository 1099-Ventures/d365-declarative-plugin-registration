using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Ten99.D365.PluginRegistration.Tool.Model;
using Ten99.D365.PluginRegistration.Tool.Reading;

namespace Ten99.D365.PluginRegistration.Tool.Dataverse;

/// <summary>Reads what is currently registered for a plugin assembly.</summary>
public sealed class RegistrationReader(IOrganizationServiceAsync2 service)
{
	private const string MessageAlias = "message";
	private const string FilterAlias = "filter";

	public async Task<CurrentState> ReadAsync(string assemblyName, CancellationToken cancellationToken)
	{
		var assembly = await ReadAssemblyAsync(assemblyName, cancellationToken);
		if (assembly is null) return CurrentState.Empty;

		var types = await ReadTypesAsync(assembly.Id, cancellationToken);
		var steps = types.Count == 0 ? [] : await ReadStepsAsync(types.Select(t => t.Id), cancellationToken);

		return new CurrentState(assembly, types, steps);
	}

	private async Task<CurrentAssembly?> ReadAssemblyAsync(string name, CancellationToken cancellationToken)
	{
		var query = new QueryExpression("pluginassembly")
		{
			ColumnSet = new ColumnSet("pluginassemblyid", "name", "version", "content"),
			Criteria = { Conditions = { new ConditionExpression("name", ConditionOperator.Equal, name) } },
		};

		var result = await service.RetrieveMultipleAsync(query, cancellationToken);
		var entity = result.Entities.FirstOrDefault();
		if (entity is null) return null;

		var content = entity.GetAttributeValue<string>("content");
		return new CurrentAssembly(
			entity.Id,
			entity.GetAttributeValue<string>("name"),
			entity.GetAttributeValue<string>("version"),
			content is null ? null : Convert.FromBase64String(content));
	}

	private async Task<List<CurrentType>> ReadTypesAsync(Guid assemblyId, CancellationToken cancellationToken)
	{
		var query = new QueryExpression("plugintype")
		{
			ColumnSet = new ColumnSet("plugintypeid", "typename"),
			Criteria = { Conditions = { new ConditionExpression("pluginassemblyid", ConditionOperator.Equal, assemblyId) } },
		};

		var result = await service.RetrieveMultipleAsync(query, cancellationToken);
		return result.Entities.Select(e => new CurrentType(e.Id, e.GetAttributeValue<string>("typename"))).ToList();
	}

	private async Task<List<CurrentStep>> ReadStepsAsync(IEnumerable<Guid> typeIds, CancellationToken cancellationToken)
	{
		var query = new QueryExpression("sdkmessageprocessingstep")
		{
			ColumnSet = new ColumnSet("sdkmessageprocessingstepid", "name", "stage", "mode", "rank", "filteringattributes", "plugintypeid"),
			Criteria = { Conditions = { new ConditionExpression("plugintypeid", ConditionOperator.In, typeIds.Cast<object>().ToArray()) } },
		};

		query.LinkEntities.Add(new LinkEntity("sdkmessageprocessingstep", "sdkmessage", "sdkmessageid", "sdkmessageid", JoinOperator.Inner)
		{
			EntityAlias = MessageAlias,
			Columns = new ColumnSet("name"),
		});

		query.LinkEntities.Add(new LinkEntity("sdkmessageprocessingstep", "sdkmessagefilter", "sdkmessagefilterid", "sdkmessagefilterid", JoinOperator.LeftOuter)
		{
			EntityAlias = FilterAlias,
			Columns = new ColumnSet("primaryobjecttypecode"),
		});

		var result = await service.RetrieveMultipleAsync(query, cancellationToken);
		var images = result.Entities.Count == 0 ? [] : await ReadImagesAsync(result.Entities.Select(e => e.Id), cancellationToken);

		return result.Entities.Select(e =>
		{
			var entity = Aliased<string>(e, FilterAlias, "primaryobjecttypecode");
			return new CurrentStep(
				e.Id,
				e.GetAttributeValue<EntityReference>("plugintypeid").Id,
				e.GetAttributeValue<string>("name"),
				Aliased<string>(e, MessageAlias, "name") ?? "",
				string.IsNullOrEmpty(entity) || entity == "none" ? null : entity,
				(Stage)e.GetAttributeValue<OptionSetValue>("stage").Value,
				(Mode)e.GetAttributeValue<OptionSetValue>("mode").Value,
				e.GetAttributeValue<int>("rank"),
				SplitAttributes(e.GetAttributeValue<string>("filteringattributes")),
				images.TryGetValue(e.Id, out var stepImages) ? stepImages : []);
		}).ToList();
	}

	private async Task<Dictionary<Guid, List<CurrentImage>>> ReadImagesAsync(IEnumerable<Guid> stepIds, CancellationToken cancellationToken)
	{
		var query = new QueryExpression("sdkmessageprocessingstepimage")
		{
			ColumnSet = new ColumnSet("sdkmessageprocessingstepimageid", "sdkmessageprocessingstepid", "imagetype", "entityalias", "name", "attributes"),
			Criteria = { Conditions = { new ConditionExpression("sdkmessageprocessingstepid", ConditionOperator.In, stepIds.Cast<object>().ToArray()) } },
		};

		var result = await service.RetrieveMultipleAsync(query, cancellationToken);
		return result.Entities
			.GroupBy(e => e.GetAttributeValue<EntityReference>("sdkmessageprocessingstepid").Id)
			.ToDictionary(g => g.Key, g => g.Select(e => new CurrentImage(
				e.Id,
				(ImageType)e.GetAttributeValue<OptionSetValue>("imagetype").Value,
				e.GetAttributeValue<string>("entityalias") ?? "",
				e.GetAttributeValue<string>("name") ?? "",
				SplitAttributes(e.GetAttributeValue<string>("attributes")))).ToList());
	}

	private static T? Aliased<T>(Entity entity, string alias, string attribute) =>
		entity.GetAttributeValue<AliasedValue>($"{alias}.{attribute}")?.Value is T value ? value : default;

	internal static IReadOnlyList<string>? SplitAttributes(string? value) =>
		DeclarationBinder.Normalise(value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
