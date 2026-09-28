using System;

// Stand-ins so the samples compile without the Dataverse SDK or a real plugin project.
namespace Samples
{
	public interface IPlugin
	{
		void Execute(IServiceProvider serviceProvider);
	}

	public abstract class SamplePlugin : IPlugin
	{
		public void Execute(IServiceProvider serviceProvider) { }
	}

	// Late-bound: hand-maintained constants
	internal static class EntityLogicalNames
	{
		internal const string Contact = "contact";
		internal const string Lead = "lead";
		internal const string KnowledgeArticle = "knowledgearticle";
	}

	internal static class ContactFieldNames
	{
		internal const string EmailAddress1 = "emailaddress1";
	}

	internal static class LeadFieldNames
	{
		internal const string FirstName = "firstname";
		internal const string LastName = "lastname";
		internal const string CompanyName = "companyname";
		internal const string Subject = "subject";
		internal const string ParentAccountId = "parentaccountid";
	}

	// Early-bound: the shape pac modelbuilder emits with emitFieldsClasses
	public partial class Lead
	{
		public const string EntityLogicalName = "lead";

		public partial class Fields
		{
			public const string FirstName = "firstname";
			public const string LastName = "lastname";
			public const string CompanyName = "companyname";
			public const string Subject = "subject";
			public const string ParentAccountId = "parentaccountid";
		}
	}

	public partial class KnowledgeArticle
	{
		public const string EntityLogicalName = "knowledgearticle";
	}
}
