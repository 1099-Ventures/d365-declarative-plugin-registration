using System.CommandLine;
using Ten99.D365.PluginRegistration.Tool.Dataverse;
using Ten99.D365.PluginRegistration.Tool.Exporting;

namespace Ten99.D365.PluginRegistration.Tool.Commands;

internal static class ExportCommand
{
	public static Command Create()
	{
		var assemblyName = new Option<string>("--assembly-name", "-n")
		{
			Description = "Name of the registered plugin assembly, e.g. Contoso.Plugins.",
			Required = true,
		};

		var environment = new Option<string>("--environment", "-e")
		{
			Description = "Dataverse environment URL, e.g. https://org.crm.dynamics.com.",
			Required = true,
		};

		var auth = new Option<AuthMode>("--auth")
		{
			Description = "How to sign in: Interactive (browser), DeviceCode, or AzureCli (pipelines).",
			DefaultValueFactory = _ => AuthMode.Interactive,
		};

		var tenant = new Option<string?>("--tenant")
		{
			Description = "Entra tenant id or domain. Defaults to the signed-in account's home tenant.",
		};

		var output = new Option<FileInfo?>("--output", "-o")
		{
			Description = "Write the attributes to this file instead of the console.",
		};

		var command = new Command("export", "Print the attributes that declare what is registered now, to adopt existing steps.")
		{
			assemblyName, environment, auth, tenant, output,
		};

		command.SetAction(async (parseResult, cancellationToken) =>
		{
			var uri = Connection.ParseEnvironment(parseResult.GetValue(environment)!);
			var client = Connection.Connect(uri, Connection.CreateCredential(parseResult.GetValue(auth), parseResult.GetValue(tenant)));

			var name = parseResult.GetValue(assemblyName)!;
			var state = await new RegistrationReader(client).ReadAsync(name, cancellationToken);
			if (state.Assembly is null)
			{
				Console.Error.WriteLine($"Assembly {name} is not registered in {uri}.");
				return 1;
			}

			var text = Exporter.Export(state, uri);
			if (parseResult.GetValue(output) is { } file)
			{
				await File.WriteAllTextAsync(file.FullName, text, cancellationToken);
				Console.WriteLine($"Wrote {state.Steps.Count} step(s) for {state.Types.Count} type(s) to {file.FullName}");
			}
			else
			{
				Console.Write(text);
			}

			return 0;
		});

		return command;
	}
}
