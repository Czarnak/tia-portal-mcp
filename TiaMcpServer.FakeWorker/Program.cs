using System.Text.Json;

// Scripted stand-in for TiaMcpServer.OpennessWorker used by IPC integration tests.
// The test encodes the scenario in the request's projectPath field.
var line = Console.In.ReadLine();
if (line is null)
{
    return;
}

string? scenario = null;
try
{
    using var doc = JsonDocument.Parse(line);
    if (doc.RootElement.TryGetProperty("projectPath", out var p))
    {
        scenario = p.GetString();
    }
}
catch (JsonException)
{
    scenario = "malformed-request";
}

switch (scenario)
{
    case "ok":
        Respond("""{"success":true,"payload":"{\"hello\":true}"}""");
        break;
    case "ok-with-stderr":
        Console.Error.WriteLine("Skipping device 'X' while reading hardware configuration: access denied.");
        Console.Error.WriteLine("Skipping subnet 'Y' while reading hardware configuration: not supported.");
        Respond("""{"success":true,"payload":"{\"hello\":true}"}""");
        break;
    case "error-prefix-payload":
        Respond("""{"success":true,"payload":"Error: literal payload text, not a failure"}""");
        break;
    case "worker-error":
        Respond("""{"success":false,"error":"boom"}""");
        break;
    case "malformed":
        Console.Out.WriteLine("this is not json");
        Console.Out.Flush();
        break;
    case "silent-exit":
        Console.Error.WriteLine("worker crashed during attach");
        break;
    default:
        Respond($$"""{"success":false,"error":"unknown scenario '{{scenario}}'"}""");
        break;
}

void Respond(string json)
{
    Console.Out.WriteLine(json);
    Console.Out.Flush();
}
