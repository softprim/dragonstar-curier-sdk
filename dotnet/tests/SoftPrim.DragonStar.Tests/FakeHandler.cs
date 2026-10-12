using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SoftPrim.DragonStar.Tests
{
    /// <summary>A recorded request (body captured before the SDK disposes the message).</summary>
    public sealed class RecordedRequest
    {
        public RecordedRequest(HttpRequestMessage message, string? body)
        {
            Method = message.Method;
            Uri = message.RequestUri!;
            Headers = message.Headers;
            ContentType = message.Content?.Headers.ContentType?.MediaType;
            Body = body;
        }

        public HttpMethod Method { get; }
        public Uri Uri { get; }
        public HttpRequestHeaders Headers { get; }
        public string? ContentType { get; }
        public string? Body { get; }
    }

    /// <summary>Fake HTTP handler with scripted responses, served in order.</summary>
    public sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Queue<Func<RecordedRequest, HttpResponseMessage>> _script = new();

        public List<RecordedRequest> Requests { get; } = new();

        public FakeHandler Enqueue(Func<RecordedRequest, HttpResponseMessage> responder)
        {
            _script.Enqueue(responder);
            return this;
        }

        public FakeHandler Json(HttpStatusCode status, string json, Action<HttpResponseMessage>? configure = null)
            => Enqueue(_ =>
            {
                var r = new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
                configure?.Invoke(r);
                return r;
            });

        public FakeHandler Problem(int status, string code, string? detail = null, string? retryAfter = null, string? extra = null)
            => Enqueue(_ =>
            {
                var json = "{\"type\":\"https://api.curierdragonstar.ro/errors/" + code.Replace('_', '-') + "\","
                           + "\"title\":\"Error\",\"status\":" + status + ",\"code\":\"" + code + "\""
                           + (detail != null ? ",\"detail\":\"" + detail + "\"" : "")
                           + (extra != null ? "," + extra : "")
                           + "}";
                var r = new HttpResponseMessage((HttpStatusCode)status)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/problem+json"),
                };
                if (retryAfter != null) r.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
                return r;
            });

        public FakeHandler Bytes(byte[] bytes, string contentType = "application/pdf")
            => Enqueue(_ =>
            {
                var content = new ByteArrayContent(bytes);
                content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            });

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string? body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var recorded = new RecordedRequest(request, body);
            Requests.Add(recorded);
            if (_script.Count == 0)
                throw new InvalidOperationException("Unexpected request: " + request.Method + " " + request.RequestUri);
            var response = _script.Dequeue()(recorded);
            response.RequestMessage = request;
            return response;
        }
    }
}
