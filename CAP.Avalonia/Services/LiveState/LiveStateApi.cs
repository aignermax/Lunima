using System.Text.Json;
using Avalonia.Threading;
using CAP_Core;
using CAP.Avalonia.ViewModels;

namespace CAP.Avalonia.Services.LiveState;

/// <summary>
/// Routes agent requests (method + path + JSON body) to live app state reads and
/// commands. Every handler runs on the UI thread; the HTTP layer stays thread-agnostic.
/// </summary>
public class LiveStateApi
{
    private const int StatusOk = 200;
    private const int StatusBadRequest = 400;
    private const int StatusNotFound = 404;
    private const int StatusServerError = 500;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly Func<MainViewModel> _mainViewModel;
    private readonly ErrorConsoleService _errorConsole;
    private readonly LiveStateCommandExecutor _executor;
    private readonly Func<Func<Task<object?>>, Task<object?>> _invokeOnUiThread;

    /// <summary>
    /// Initializes the API.
    /// </summary>
    /// <param name="mainViewModel">Lazy accessor for the app's MainViewModel (avoids DI cycles).</param>
    /// <param name="errorConsole">Error console whose entries are exposed to agents.</param>
    /// <param name="executor">Executor for state-changing commands.</param>
    /// <param name="invokeOnUiThread">
    /// UI-thread marshaller; defaults to the Avalonia dispatcher. Tests inject a direct invoker.
    /// </param>
    public LiveStateApi(
        Func<MainViewModel> mainViewModel,
        ErrorConsoleService errorConsole,
        LiveStateCommandExecutor executor,
        Func<Func<Task<object?>>, Task<object?>>? invokeOnUiThread = null)
    {
        _mainViewModel = mainViewModel;
        _errorConsole = errorConsole;
        _executor = executor;
        _invokeOnUiThread = invokeOnUiThread ?? DispatcherInvoke;
    }

    private static Task<object?> DispatcherInvoke(Func<Task<object?>> handler) =>
        Dispatcher.UIThread.CheckAccess() ? handler() : Dispatcher.UIThread.InvokeAsync(handler);

    /// <summary>
    /// Handles one request and returns HTTP status code + JSON response body.
    /// Never throws — errors map to 4xx/5xx JSON payloads.
    /// </summary>
    public async Task<(int StatusCode, string Json)> HandleAsync(string method, string path, string body)
    {
        try
        {
            var result = await DispatchAsync(method.ToUpperInvariant(), path.TrimEnd('/'), body);
            return result;
        }
        catch (JsonException ex)
        {
            return (StatusBadRequest, ErrorJson($"Invalid JSON body: {ex.Message}"));
        }
        catch (Exception ex)
        {
            return (StatusServerError, ErrorJson($"{ex.GetType().Name}: {ex.Message}"));
        }
    }

    private async Task<(int, string)> DispatchAsync(string method, string path, string body)
    {
        switch ($"{method} {path}")
        {
            case "GET /status":
                return Ok(await OnUi(vm => (object?)new
                {
                    App = "Lunima",
                    DesignFile = vm.FileOperations.CurrentFilePath,
                    ComponentCount = vm.Canvas.Components.Count,
                    ConnectionCount = vm.Canvas.Connections.Count,
                    IsRouting = vm.Canvas.IsRouting,
                }));
            case "GET /design":
                return Ok(await OnUi(vm => (object?)LiveStateSnapshotBuilder.BuildDesign(vm)));
            case "GET /errors":
                return Ok(await OnUi(_ => (object?)LiveStateSnapshotBuilder.BuildErrors(_errorConsole)));
            case "GET /simulation":
                return Ok(await OnUi(vm => (object?)LiveStateSnapshotBuilder
                    .BuildSimulation(vm, _executor.LastSimulationResult)));
            case "POST /simulation/run":
                return Ok(await OnUiAsync(async vm => (object?)await _executor.RunSimulationAsync(vm)));
            case "POST /component/move":
            {
                var result = await ParseMoveAndExecuteAsync(body);
                return result == null
                    ? BadRequest("Body must be {\"componentId\": \"...\", \"x\": 0.0, \"y\": 0.0}")
                    : Ok(result);
            }
            case "POST /design/load":
            {
                string? file = ReadString(body, "path");
                if (file == null) return BadRequest("Body must be {\"path\": \"/abs/file.lun\"}");
                return Ok(await OnUiAsync(async vm => (object?)await _executor.LoadDesignAsync(vm, file)));
            }
            case "POST /screenshot":
            {
                string? file = string.IsNullOrWhiteSpace(body) ? null : ReadString(body, "path");
                return Ok(await OnUi(_ => (object?)_executor.CaptureScreenshot(file)));
            }
            default:
                return (StatusNotFound, ErrorJson(
                    $"Unknown route '{method} {path}'. Routes: GET /status /design /errors /simulation; " +
                    "POST /simulation/run /component/move /design/load /screenshot"));
        }
    }

    private async Task<object?> ParseMoveAndExecuteAsync(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (!root.TryGetProperty("componentId", out var idProp)
            || !root.TryGetProperty("x", out var xProp)
            || !root.TryGetProperty("y", out var yProp))
            return null;

        string id = idProp.GetString() ?? "";
        double x = xProp.GetDouble();
        double y = yProp.GetDouble();
        return await OnUi(vm => (object?)_executor.MoveComponent(vm, id, x, y));
    }

    /// <summary>Reads a string property from a JSON object body; null when absent.</summary>
    private static string? ReadString(string body, string propertyName)
    {
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty(propertyName, out var prop) ? prop.GetString() : null;
    }

    private Task<object?> OnUi(Func<MainViewModel, object?> handler) =>
        _invokeOnUiThread(() => Task.FromResult(handler(_mainViewModel())));

    private Task<object?> OnUiAsync(Func<MainViewModel, Task<object?>> handler) =>
        _invokeOnUiThread(() => handler(_mainViewModel()));

    private static (int, string) Ok(object? payload) =>
        (StatusOk, JsonSerializer.Serialize(payload, JsonOptions));

    private static (int, string) BadRequest(string message) => (StatusBadRequest, ErrorJson(message));

    private static string ErrorJson(string message) =>
        JsonSerializer.Serialize(new { Error = message }, JsonOptions);
}
