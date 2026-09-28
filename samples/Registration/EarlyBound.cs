using DeclarativePluginRegistration;

// Early-bound project: the entity is a type, and attribute names come from the generated Fields class.
namespace Samples.EarlyBound
{
	// One step, no image
	[PluginStep(Message.Create, typeof(KnowledgeArticle), Stage.PreOperation)]
	public class SetKnowledgeArticleSlug : SamplePlugin { }

	// Two steps, post-image on Update
	[PluginStep(Message.Create, typeof(Lead), Stage.PreOperation)]
	[PluginStep(Message.Update, typeof(Lead), Stage.PostOperation,
		FilteringAttributes = new[] { Lead.Fields.FirstName, Lead.Fields.LastName, Lead.Fields.CompanyName })]
	[PluginImage(Message.Update, ImageType.PostImage,
		Lead.Fields.FirstName, Lead.Fields.LastName, Lead.Fields.CompanyName)]
	public class SetLeadSubject : SamplePlugin { }

	// Two Update steps on one class: Key and Step disambiguate the images
	[PluginStep(Message.Update, typeof(Lead), Stage.PreOperation, Key = "validate",
		FilteringAttributes = new[] { Lead.Fields.ParentAccountId })]
	[PluginStep(Message.Update, typeof(Lead), Stage.PostOperation, Key = "sync", Mode = Mode.Asynchronous)]
	[PluginImage(Message.Update, ImageType.PreImage, Lead.Fields.ParentAccountId, Step = "validate")]
	[PluginImage(Message.Update, ImageType.PostImage, Step = "sync")]
	public class LeadAccountSync : SamplePlugin { }
}
