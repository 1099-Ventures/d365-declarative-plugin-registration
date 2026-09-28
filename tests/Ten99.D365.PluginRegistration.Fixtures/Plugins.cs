using System;
using Microsoft.Xrm.Sdk;
using Ten99.D365.PluginRegistration;

namespace Fixtures
{
	public abstract class PluginBase : IPlugin
	{
		public void Execute(IServiceProvider serviceProvider) { }
	}

	public partial class Lead : Entity
	{
		public const string EntityLogicalName = "lead";

		public Lead() : base(EntityLogicalName) { }

		public static class Fields
		{
			public const string FirstName = "firstname";
			public const string LastName = "lastname";
		}
	}

	[PluginStep(Message.Create, "knowledgearticle", Stage.PreOperation)]
	public class LateBoundSingleStep : PluginBase { }

	[PluginStep(Message.Create, typeof(Lead), Stage.PreOperation)]
	[PluginStep(Message.Update, typeof(Lead), Stage.PostOperation, Mode = Mode.Asynchronous, Order = 5,
		FilteringAttributes = new[] { Lead.Fields.LastName, Lead.Fields.FirstName })]
	[PluginImage(Message.Update, ImageType.PostImage, Lead.Fields.FirstName, Lead.Fields.LastName)]
	public class EarlyBoundWithImage : PluginBase { }

	[PluginStep(Message.Update, "contact", Stage.PreOperation, Key = "validate", Id = "5b7c2f1e-0d4a-4c3b-9e8f-2a6d1c0b9e71")]
	[PluginStep(Message.Update, "contact", Stage.PostOperation, Key = "sync")]
	[PluginImage(Message.Update, ImageType.PreImage, "emailaddress1", Step = "validate", Alias = "before")]
	[PluginImage(Message.Update, ImageType.PostImage, Step = "sync")]
	public class KeyedSteps : PluginBase { }

	[PluginStep(Message.Associate, Stage.PostOperation)]
	public class NoEntity : PluginBase { }

	public class Undeclared : PluginBase { }

	public class NotAPlugin { }
}
