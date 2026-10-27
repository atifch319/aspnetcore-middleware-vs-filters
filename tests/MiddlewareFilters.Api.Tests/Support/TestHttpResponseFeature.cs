using System.IO.Pipelines;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace MiddlewareFilters.Api.Tests.Support;

/// <summary>
/// Response feature that runs <c>OnStarting</c> callbacks and reports <c>HasStarted</c>.
/// Replacing <c>HttpResponse.Body</c> on <c>DefaultHttpContext</c> skips both of those steps.
/// </summary>
internal sealed class TestHttpResponseFeature : IHttpResponseFeature, IHttpResponseBodyFeature
{
    private readonly MemoryStream _body = new();
    private readonly List<(Func<object, Task> Callback, object State)> _onStarting = [];
    private PipeWriter? _writer;
    private bool _hasStarted;

    public int StatusCode { get; set; } = StatusCodes.Status200OK;

    public string? ReasonPhrase { get; set; }

    public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

    public Stream Stream => _body;

    Stream IHttpResponseFeature.Body
    {
        get => _body;
        set => throw new NotSupportedException("The test response body is fixed.");
    }

    public PipeWriter Writer => _writer ??= PipeWriter.Create(_body, new StreamPipeWriterOptions(leaveOpen: true));

    public bool HasStarted => _hasStarted;

    public void OnStarting(Func<object, Task> callback, object state)
    {
        if (_hasStarted)
        {
            throw new InvalidOperationException("The response has already started.");
        }

        _onStarting.Add((callback, state));
    }

    public void OnCompleted(Func<object, Task> callback, object state)
    {
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_hasStarted)
        {
            return;
        }

        var callbacks = _onStarting.ToArray();
        _onStarting.Clear();

        foreach (var (callback, state) in callbacks)
        {
            await callback(state);
        }

        _hasStarted = true;
    }

    public async Task CompleteAsync()
    {
        await StartAsync();
    }

    public Task SendFileAsync(string path, long offset, long? count, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    public void DisableBuffering()
    {
    }
}
