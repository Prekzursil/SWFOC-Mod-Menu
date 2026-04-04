using System.Net;
using System.Net.Http.Headers;

namespace SwfocTrainer.Tests.Common;

internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly IReadOnlyDictionary<string, (string ContentType, byte[] Body)> _responses;

    public StubHttpMessageHandler(IReadOnlyDictionary<string, (string ContentType, byte[] Body)> responses)
    {
        _responses = responses ?? throw new ArgumentNullException(nameof(responses));
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var uri = request.RequestUri ?? throw new InvalidOperationException("Request URI is null.");
        var key = uri.ToString();
        if (!_responses.TryGetValue(key, out var payload))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("not found")
            });
        }

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload.Body)
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(payload.ContentType);
        return Task.FromResult(response);
    }
}
