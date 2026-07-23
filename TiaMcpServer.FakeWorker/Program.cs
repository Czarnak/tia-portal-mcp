using System.Text.Json;

// Scripted stand-in for TiaMcpServer.OpennessWorker used by IPC integration tests.
// Mirrors the real worker's request loop: one JSON line in, one JSON line out, until
// stdin closes. The test encodes the scenario in the request's projectPath field.
var seq = 0;
string? line;
while ((line = Console.In.ReadLine()) is not null)
{
    seq++;
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
            // seq proves whether two requests hit the same process (2.1 reuse/restart tests).
            Respond($$"""{"success":true,"payload":"{\"seq\":{{seq}}}"}""");
            break;
        case "ok-with-resolved-path":
            Respond("""{"success":true,"payload":"{}","resolvedProjectPath":"C:\\resolved\\Ground.ap21"}""");
            break;
        case "C:\\bound\\Session.ap21":
            // Used by the "already bound but worker reports a different project" test: the
            // session is pre-bound to this literal path (see IsSameProject/TryResolve), so a
            // request without an explicit projectPath forwards this exact string as the
            // scenario key. Reports a DIFFERENT resolvedProjectPath to simulate divergence.
            Respond("""{"success":true,"payload":"{}","resolvedProjectPath":"C:\\actual\\Other.ap21"}""");
            break;
        case "C:\\stable\\Project.ap21":
            // Used by the "already bound, worker reports the SAME project" test: reports its
            // own scenario key back as resolvedProjectPath - no divergence, no warning expected.
            Respond("""{"success":true,"payload":"{}","resolvedProjectPath":"C:\\stable\\Project.ap21"}""");
            break;
        case "C:\\equivalent\\Project.ap21":
            // Used by the "equivalent but differently-spelled path" divergence test (Finding 2):
            // reports the identical project via forward slashes instead of back slashes. A raw
            // string.Equals would misclassify this as divergence; the canonicalized comparison
            // (ProjectSessionBinding.IsBoundTo) must not.
            Respond("""{"success":true,"payload":"{}","resolvedProjectPath":"C:/equivalent/Project.ap21"}""");
            break;
        case "ok-with-warnings":
            Respond("""{"success":true,"payload":"{\"hello\":true}","warnings":["Skipping device 'X' while reading hardware configuration: access denied.","Skipping subnet 'Y' while reading hardware configuration: not supported."]}""");
            break;
        case "ok-with-stderr":
            // Stderr between/during requests is host-log-only now; it must NOT surface as warnings.
            Console.Error.WriteLine("orphan stderr line: attach diagnostics");
            Console.Error.Flush();
            Respond("""{"success":true,"payload":"{\"hello\":true}"}""");
            break;
        case "error-prefix-payload":
            Respond("""{"success":true,"payload":"Error: literal payload text, not a failure"}""");
            break;
        case "worker-error":
            Respond("""{"success":false,"error":"boom"}""");
            break;
        case "worker-error-with-category":
            // Proves OpennessWorkerClient.InvokeWorkerAsync preserves an approved
            // worker-reported category instead of overwriting it with worker_operation_failed.
            Respond("""{"success":false,"error":"invalid value","failureCategory":"validation_error"}""");
            break;
        case "malformed":
            Console.Out.WriteLine("this is not json");
            Console.Out.Flush();
            break;
        case "null-response":
            Console.Out.WriteLine("null");
            Console.Out.Flush();
            break;
        case "crash":
            Console.Error.WriteLine("worker crashed during attach");
            Console.Error.Flush();
            return;
        case "hang":
            Thread.Sleep(Timeout.Infinite);
            break;
        case "echo":
            // Returns the received request verbatim so tests can assert which fields survived
            // the BatchOperationRequest -> WorkerRequest hop.
            Respond(JsonSerializer.Serialize(new { success = true, payload = line }));
            break;
        case "direct-status-only":
            // Used to prove the direct get_project_status MCP tool routes through the
            // GetProjectStatusAsync operation only, never the internal lifecycle probe.
            Respond(ReadMethod(line) == "get_project_status"
                ? """{"success":true,"payload":"{\"isOpen\":true}"}"""
                : $$"""{"success":false,"error":"expected get_project_status, got '{{ReadMethod(line)}}'"}""");
            break;
        case "status-no-project":
            // Simulates the real worker's GetStatusReadOnly when nothing is open and no path
            // was requested: isOpen:false, no resolvedProjectPath - nothing was opened.
            Respond("""{"success":true,"payload":"{\"isOpen\":false}"}""");
            break;
        case "lifecycle-probe-only":
            // Guards against a regression where a save/save-as/archive/close current-state
            // read reverts to the direct status operation: fails ONLY when the request used
            // get_project_status; every other operation (the probe itself, or the tool's own
            // write call that follows) succeeds normally so the full preview/apply round trip
            // can complete.
            Respond(ReadMethod(line) == "get_project_status"
                ? """{"success":false,"error":"current-state read must use probe_project_status_for_lifecycle, not get_project_status"}"""
                : """{"success":true,"payload":"{\"isOpen\":true}"}""");
            break;
        default:
            Respond($$"""{"success":false,"error":"unknown scenario '{{scenario}}'"}""");
            break;
    }
}

void Respond(string json)
{
    Console.Out.WriteLine(json);
    Console.Out.Flush();
}

string? ReadMethod(string requestLine)
{
    try
    {
        using var doc = JsonDocument.Parse(requestLine);
        return doc.RootElement.TryGetProperty("method", out var method) ? method.GetString() : null;
    }
    catch (JsonException)
    {
        return null;
    }
}
