using Azure.Core;
using Azure.Identity;
using Microsoft.PowerPlatform.Dataverse.Client;

namespace Ten99.D365.PluginRegistration.Tool.Dataverse;

public enum AuthMode
{
	/// <summary>Browser sign-in. For local use.</summary>
	Interactive,

	/// <summary>Device code sign-in, for terminals without a browser.</summary>
	DeviceCode,

	/// <summary>The Azure CLI's signed-in identity. In ADO, run inside an AzureCLI task with a workload identity federation service connection.</summary>
	AzureCli,
}

public static class Connection
{
	private static readonly TimeSpan SignInTimeout = TimeSpan.FromMinutes(5);

	public static TokenCredential CreateCredential(AuthMode mode, string? tenantId) => mode switch
	{
		AuthMode.Interactive => new InteractiveBrowserCredential(new InteractiveBrowserCredentialOptions { TenantId = tenantId }),
		AuthMode.DeviceCode => new DeviceCodeCredential(new DeviceCodeCredentialOptions
		{
			TenantId = tenantId,
			DeviceCodeCallback = (code, _) =>
			{
				Console.Error.WriteLine(code.Message);
				return Task.CompletedTask;
			},
		}),
		AuthMode.AzureCli => new AzureCliCredential(new AzureCliCredentialOptions { TenantId = tenantId }),
		_ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
	};

	public static ServiceClient Connect(Uri environment, TokenCredential credential)
	{
		var scope = $"{environment.GetLeftPart(UriPartial.Authority)}/.default";

		var client = new ServiceClient(
			environment,
			async _ =>
			{
				//	Sign-in can wait on a person. Give up rather than hang on an abandoned prompt.
				using var timeout = new CancellationTokenSource(SignInTimeout);
				return (await credential.GetTokenAsync(new TokenRequestContext([scope]), timeout.Token)).Token;
			},
			useUniqueInstance: true);

		if (!client.IsReady)
		{
			throw new InvalidOperationException($"Could not connect to {environment}: {client.LastError}", client.LastException);
		}

		return client;
	}

	/// <summary>Accepts "org.crm.dynamics.com" as well as a full URL.</summary>
	public static Uri ParseEnvironment(string value) =>
		new(value.Contains("://", StringComparison.Ordinal) ? value : $"https://{value}");
}
