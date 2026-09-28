using DeclarativePluginRegistration;

// Late-bound project: entity and attribute names come from hand-maintained constants.
namespace Samples.LateBound
{
	// One step, no image
	[PluginStep(Message.Create, EntityLogicalNames.KnowledgeArticle, Stage.PreOperation)]
	public class SetKnowledgeArticleSlug : SamplePlugin { }

	// Two steps, post-image on Update
	[PluginStep(Message.Create, EntityLogicalNames.Lead, Stage.PreOperation)]
	[PluginStep(Message.Update, EntityLogicalNames.Lead, Stage.PostOperation,
		FilteringAttributes = new[] { LeadFieldNames.FirstName, LeadFieldNames.LastName, LeadFieldNames.CompanyName })]
	[PluginImage(Message.Update, ImageType.PostImage,
		LeadFieldNames.FirstName, LeadFieldNames.LastName, LeadFieldNames.CompanyName)]
	public class SetLeadSubject : SamplePlugin { }

	// Two steps, pre-image on Update, explicit Id adopting an existing step
	[PluginStep(Message.Create, EntityLogicalNames.Contact, Stage.PreValidation)]
	[PluginStep(Message.Update, EntityLogicalNames.Contact, Stage.PreValidation,
		FilteringAttributes = new[] { ContactFieldNames.EmailAddress1 },
		Id = "5b7c2f1e-0d4a-4c3b-9e8f-2a6d1c0b9e71")]
	[PluginImage(Message.Update, ImageType.PreImage, ContactFieldNames.EmailAddress1)]
	public class PreventDuplicateContacts : SamplePlugin { }

	// No primary entity: relationship messages
	[PluginStep(Message.Associate, Stage.PostOperation)]
	[PluginStep(Message.Disassociate, Stage.PostOperation)]
	public class SyncMarketingListMembers : SamplePlugin { }
}
