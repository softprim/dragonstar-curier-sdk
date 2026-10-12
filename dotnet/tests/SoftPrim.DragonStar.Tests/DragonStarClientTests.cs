using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace SoftPrim.DragonStar.Tests
{
    public class DragonStarClientTests
    {
        private const string Base = "http://fake.local";

        private static (DragonStarClient client, FakeHandler handler) Create(int maxRetries = 2)
        {
            var handler = new FakeHandler();
            var http = new HttpClient(handler);
            var client = new DragonStarClient("test-key", http, new DragonStarClientOptions
            {
                BaseUrl = Base + "/",
                MaxRetries = maxRetries,
                Timeout = TimeSpan.FromSeconds(5),
            });
            return (client, handler);
        }

        // 1. Authorization + User-Agent
        [Fact]
        public async Task SendsAuthorizationAndUserAgentHeaders()
        {
            var (client, handler) = Create();
            handler.Json(HttpStatusCode.OK, "{\"masterId\":454,\"count\":1,\"data\":[{\"branchId\":454,\"name\":\"Sediu\",\"county\":\"Cluj\",\"city\":\"Cluj-Napoca\",\"address\":\"Str. X 1\"}]}");

            var branches = await client.GetBranchesAsync();

            var req = Assert.Single(handler.Requests);
            Assert.Equal("Bearer", req.Headers.Authorization!.Scheme);
            Assert.Equal("test-key", req.Headers.Authorization.Parameter);
            Assert.Equal("softprim-dragonstar-dotnet/1.0.0", string.Join(" ", req.Headers.GetValues("User-Agent")));
            Assert.Contains(req.Headers.Accept, a => a.MediaType == "application/json");
            Assert.Equal(Base + "/v1/branches", req.Uri.ToString());
            Assert.Equal(454, branches.MasterId);
            Assert.Equal("Cluj-Napoca", branches.Data[0].City);
        }

        // 2. createAwb sends correct JSON and returns the AWB
        [Fact]
        public async Task CreateAwbSendsJsonAndReturnsAwb()
        {
            var (client, handler) = Create();
            handler.Json(HttpStatusCode.Created,
                "{\"awb\":94348148,\"totalAmount\":9.83,\"weight\":5,\"pieces\":1,\"codAmount\":100,\"codPaymentType\":\"account\"}");

            var created = await client.CreateAwbAsync(new AwbCreateRequest
            {
                ShipmentType = ShipmentType.Parcel,
                Weight = 5,
                Pieces = 1,
                RecipientName = "Ion Popescu",
                RecipientAddress = "Str. Florilor 1",
                RecipientCounty = "Cluj",
                RecipientCity = "Cluj-Napoca",
                CodAmount = 100,
                CodPaymentType = CodPaymentType.Cash,
            });

            var req = Assert.Single(handler.Requests);
            Assert.Equal(HttpMethod.Post, req.Method);
            Assert.Equal(Base + "/v1/awb", req.Uri.ToString());
            Assert.Equal("application/json", req.ContentType);

            using var doc = JsonDocument.Parse(req.Body!);
            var root = doc.RootElement;
            Assert.Equal("parcel", root.GetProperty("shipmentType").GetString());
            Assert.Equal(5, root.GetProperty("weight").GetDecimal());
            Assert.Equal(1, root.GetProperty("pieces").GetInt32());
            Assert.Equal("Ion Popescu", root.GetProperty("recipientName").GetString());
            Assert.Equal("Cluj", root.GetProperty("recipientCounty").GetString());
            Assert.Equal("cash", root.GetProperty("codPaymentType").GetString());
            // nulls are omitted
            Assert.False(root.TryGetProperty("recipientPhone", out _));
            Assert.False(root.TryGetProperty("length", out _));
            Assert.False(root.TryGetProperty("pcId", out _));

            Assert.Equal(94348148, created.Awb);
            Assert.Equal(9.83m, created.TotalAmount);
            Assert.Equal("account", created.CodPaymentType);
        }

        [Fact]
        public async Task CreateAwbForBranchAddsPcId()
        {
            var (client, handler) = Create();
            handler.Json(HttpStatusCode.Created, "{\"awb\":1,\"totalAmount\":1}");

            await client.CreateAwbForBranchAsync(454, new AwbCreateRequest
            {
                ShipmentType = ShipmentType.Envelope,
                RecipientName = "Ion",
                RecipientAddress = "Str. 1",
                RecipientCounty = "Cluj",
                RecipientCity = "Dej",
            });

            var req = Assert.Single(handler.Requests);
            Assert.Equal(Base + "/v1/awb/branch", req.Uri.ToString());
            using var doc = JsonDocument.Parse(req.Body!);
            Assert.Equal(454, doc.RootElement.GetProperty("pcId").GetInt64());
            Assert.Equal("envelope", doc.RootElement.GetProperty("shipmentType").GetString());
        }

        // 3. 422 -> DragonStarException with code + errors
        [Fact]
        public async Task ValidationErrorBecomesDragonStarException()
        {
            var (client, handler) = Create();
            handler.Problem(422, "validation_failed", "The request has 2 invalid fields.",
                extra: "\"errors\":{\"weight\":[\"required if shipmentType = parcel or pallet\"],\"recipientCity\":[\"required\"]}");

            var ex = await Assert.ThrowsAsync<DragonStarException>(() =>
                client.QuoteAsync(new QuoteRequest { ShipmentType = ShipmentType.Parcel, RecipientCounty = "Cluj" }));

            Assert.Equal(422, ex.Status);
            Assert.Equal("validation_failed", ex.Code);
            Assert.Equal("The request has 2 invalid fields.", ex.Message);
            Assert.Equal("https://api.curierdragonstar.ro/errors/validation-failed", ex.Type);
            Assert.Equal(new[] { "required" }, ex.Errors["recipientCity"]);
            Assert.Single(ex.Errors["weight"]);
            Assert.Single(handler.Requests); // 422 is never retried
        }

        [Fact]
        public async Task ErrorWithoutBodyUsesHttpStatusMessage()
        {
            var (client, handler) = Create();
            handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

            var ex = await Assert.ThrowsAsync<DragonStarException>(() => client.TrackAsync(1));
            Assert.Equal(404, ex.Status);
            Assert.Null(ex.Code);
            Assert.Equal("HTTP 404", ex.Message);
            Assert.Empty(ex.Errors);
        }

        // 4. 429 with Retry-After: 0 is retried and succeeds (even for POST)
        [Fact]
        public async Task RateLimitIsRetriedAndSucceeds()
        {
            var (client, handler) = Create();
            handler.Problem(429, "rate_limit_exceeded", retryAfter: "0");
            handler.Json(HttpStatusCode.Created, "{\"awb\":94348148,\"totalAmount\":9.83}");

            var created = await client.CreateAwbAsync(new AwbCreateRequest
            {
                ShipmentType = ShipmentType.Envelope,
                RecipientName = "Ion",
                RecipientAddress = "Str. 1",
                RecipientCounty = "Cluj",
                RecipientCity = "Dej",
            });

            Assert.Equal(94348148, created.Awb);
            Assert.Equal(2, handler.Requests.Count);
        }

        [Fact]
        public async Task TemporarilyBannedIsNotRetried()
        {
            var (client, handler) = Create();
            handler.Problem(429, "temporarily_banned", "Too many invalid requests.", retryAfter: "600");

            var ex = await Assert.ThrowsAsync<DragonStarException>(() => client.GetCountiesAsync());
            Assert.Equal("temporarily_banned", ex.Code);
            Assert.Equal(TimeSpan.FromSeconds(600), ex.RetryAfter);
            Assert.Single(handler.Requests);
        }

        [Fact]
        public async Task RateLimitGivesUpAfterMaxRetries()
        {
            var (client, handler) = Create(maxRetries: 1);
            handler.Problem(429, "rate_limit_exceeded", retryAfter: "0");
            handler.Problem(429, "rate_limit_exceeded", retryAfter: "0");

            var ex = await Assert.ThrowsAsync<DragonStarException>(() => client.GetBranchesAsync());
            Assert.Equal(429, ex.Status);
            Assert.Equal(TimeSpan.Zero, ex.RetryAfter);
            Assert.Equal(2, handler.Requests.Count);
        }

        // 5. POST receiving 503 is NOT retried
        [Fact]
        public async Task PostWith503IsNotRetried()
        {
            var (client, handler) = Create();
            handler.Problem(503, "service_unavailable", "The service is temporarily unavailable. Please retry.", retryAfter: "0");

            var ex = await Assert.ThrowsAsync<DragonStarException>(() => client.CreateManifestAsync());
            Assert.Equal(503, ex.Status);
            Assert.Equal("service_unavailable", ex.Code);
            Assert.Single(handler.Requests);
        }

        [Fact]
        public async Task GetWith503IsRetried()
        {
            var (client, handler) = Create();
            handler.Problem(503, "service_unavailable", retryAfter: "0");
            handler.Json(HttpStatusCode.OK, "{\"count\":1,\"data\":{\"CJ\":\"Cluj\"}}");

            var counties = await client.GetCountiesAsync();
            Assert.Equal("Cluj", counties["CJ"]);
            Assert.Equal(2, handler.Requests.Count);
        }

        // 6. trackMany with 150 AWBs -> 2 requests, merged data + notFound
        [Fact]
        public async Task TrackManySplitsIntoBatchesOf100()
        {
            var (client, handler) = Create();
            var awbs = Enumerable.Range(1, 150).Select(i => (long)(94000000 + i)).ToList();

            FakeHandler Responder(int expected) => handler.Enqueue(req =>
            {
                using var doc = JsonDocument.Parse(req.Body!);
                var sent = doc.RootElement.GetProperty("awbs").EnumerateArray().Select(e => e.GetInt64()).ToList();
                Assert.Equal(expected, sent.Count);
                // first AWB found, the rest "not found" to keep the payload small
                var json = "{\"count\":1,\"data\":[{\"awb\":" + sent[0] + ",\"status\":{\"statusCode\":3,\"description\":\"Delivered\",\"descriptionRo\":\"Livrat\",\"timestamp\":\"2026-09-01T10:00:00+03:00\"}}],"
                           + "\"notFound\":[" + string.Join(",", sent.Skip(1)) + "]}";
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
                };
            });

            Responder(100);
            Responder(50);

            var result = await client.TrackManyAsync(awbs);

            Assert.Equal(2, handler.Requests.Count);
            Assert.All(handler.Requests, r => Assert.Equal(Base + "/v1/awb/tracking?events=false", r.Uri.ToString()));
            Assert.Equal(2, result.Count);
            Assert.Equal(new long[] { 94000001, 94000101 }, result.Data.Select(d => d.Awb).ToArray());
            Assert.Equal(148, result.NotFound.Count);
            Assert.Equal(ShipmentStatus.Delivered, result.Data[0].Status!.StatusCode);
            Assert.Equal(new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.FromHours(3)), result.Data[0].Status!.Timestamp);
            Assert.Null(result.Data[0].Events);
        }

        // 7. getLabel returns bytes unchanged and refuses "a4"
        [Fact]
        public async Task GetLabelReturnsBytesUnchanged()
        {
            var (client, handler) = Create();
            var pdf = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x00, 0xFF, 0x80, 0x0A, 0x0D };
            handler.Bytes(pdf);
            handler.Bytes(pdf);

            var bytes = await client.GetLabelAsync(94348148, LabelFormat.A6);
            Assert.Equal(pdf, bytes);
            Assert.Equal(Base + "/v1/awb/94348148/label/A6", handler.Requests[0].Uri.ToString());

            await client.GetLabelAsync(94348148);
            Assert.Equal(Base + "/v1/awb/94348148/label", handler.Requests[1].Uri.ToString());
        }

        [Fact]
        public async Task GetLabelRejectsLowercaseFormatBeforeRequest()
        {
            var (client, handler) = Create();
            await Assert.ThrowsAsync<ArgumentException>(() => client.GetLabelAsync(94348148, "a4"));
            Assert.Empty(handler.Requests);
        }

        [Fact]
        public async Task GetLabelZplValidatesParcelAndReturnsText()
        {
            var (client, handler) = Create();
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.GetLabelZplAsync(1, 0));
            Assert.Empty(handler.Requests);

            handler.Bytes(System.Text.Encoding.ASCII.GetBytes("^XA^FO50,50^FDTest^FS^XZ"), "application/octet-stream");
            var zpl = await client.GetLabelZplAsync(94348148, 2);
            Assert.Equal("^XA^FO50,50^FDTest^FS^XZ", zpl);
            Assert.Equal(Base + "/v1/awb/94348148/label/zpl/2", handler.Requests[0].Uri.ToString());
        }

        // 8. getLocalitiesWithKm parses "Cluj-Napoca:450"
        [Fact]
        public async Task GetLocalitiesWithKmParsesNameAndKm()
        {
            var (client, handler) = Create();
            handler.Json(HttpStatusCode.OK, "{\"count\":2,\"data\":[\"Cluj-Napoca:450\",\"Sat: Valea:12\"]}");

            var list = await client.GetLocalitiesWithKmAsync("Cluj");

            Assert.Equal(2, list.Count);
            Assert.Equal("Cluj-Napoca", list[0].Name);
            Assert.Equal(450m, list[0].Km);
            Assert.Equal("Sat: Valea", list[1].Name); // split at the LAST colon
            Assert.Equal(12m, list[1].Km);
        }

        // 9. county with diacritics / spaces is URL-encoded
        [Fact]
        public async Task CountyIsUrlEncoded()
        {
            var (client, handler) = Create();
            handler.Json(HttpStatusCode.OK, "{\"count\":1,\"data\":[\"Bistrița\"]}");
            handler.Json(HttpStatusCode.OK, "{\"count\":0,\"data\":[]}");

            var localities = await client.GetLocalitiesAsync("Bistrița-Năsăud");
            await client.GetLocalitiesWithKmAsync("Satu Mare");

            Assert.Equal("Bistrița", Assert.Single(localities));
            Assert.Equal("/v1/counties/Bistri%C8%9Ba-N%C4%83s%C4%83ud/localities", handler.Requests[0].Uri.AbsolutePath);
            Assert.Equal("/v1/counties/Satu%20Mare/localities-km", handler.Requests[1].Uri.AbsolutePath);
        }

        // ---------------------------------------------------------------- extra

        [Fact]
        public async Task TrackSendsEventsFlagAsText()
        {
            var (client, handler) = Create();
            handler.Json(HttpStatusCode.OK,
                "{\"status\":{\"statusCode\":6,\"description\":\"In transit\",\"descriptionRo\":\"In tranzit\",\"timestamp\":null},"
                + "\"events\":[{\"statusCode\":6,\"eventCode\":\"INC\",\"description\":\"Intrare Centru\",\"isException\":false,"
                + "\"timestamp\":\"2026-09-01T10:00:00+03:00\",\"location\":{\"name\":\"BACAU\",\"code\":\"BAC\"}}]}");
            handler.Json(HttpStatusCode.OK, "{\"status\":{\"statusCode\":3}}");

            var t = await client.TrackAsync(94348148);
            var t2 = await client.TrackAsync(94348148, events: false);

            Assert.Equal(Base + "/v1/awb/94348148?events=true", handler.Requests[0].Uri.ToString());
            Assert.Equal(Base + "/v1/awb/94348148?events=false", handler.Requests[1].Uri.ToString());
            Assert.Equal("BACAU", t.Events![0].Location!.Name);
            Assert.Null(t.Status!.Timestamp);
            Assert.Null(t2.Events);
            Assert.True(ShipmentStatus.IsFinal(t2.Status!.StatusCode!.Value));
        }

        [Fact]
        public async Task CreateManifestWithNothingToCloseReturnsNullId()
        {
            var (client, handler) = Create();
            handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK));
            handler.Json(HttpStatusCode.Created, "{\"manifestId\":77,\"shipmentCount\":12}");

            var empty = await client.CreateManifestAsync();
            var created = await client.CreateManifestAsync();

            Assert.Null(empty.ManifestId);
            Assert.Equal(0, empty.ShipmentCount);
            Assert.Equal(77, created.ManifestId);
            Assert.Null(handler.Requests[0].Body);
        }

        [Fact]
        public async Task CreatePickupSendsDateAsYmd()
        {
            var (client, handler) = Create();
            handler.Json(HttpStatusCode.Created, "{\"pickupId\":12345}");
            handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.Created));

            var pickup = await client.CreatePickupAsync(new PickupRequest
            {
                PickupDate = new DateTime(2026, 10, 13),
                PickupWindowStart = 10,
                PickupWindowEnd = 14,
                Parcels = 2,
                Weight = 7.5m,
            });
            var branch = await client.CreatePickupForBranchAsync(454, new PickupRequest
            {
                PickupDate = new DateTime(2026, 10, 13),
                PickupWindowStart = 10,
                PickupWindowEnd = 14,
                Pallets = 1,
                Weight = 100,
            });

            Assert.Equal(12345, pickup.PickupId);
            Assert.Null(branch);
            using var doc = JsonDocument.Parse(handler.Requests[0].Body!);
            Assert.Equal("2026-10-13", doc.RootElement.GetProperty("pickupDate").GetString());
            Assert.False(doc.RootElement.TryGetProperty("pallets", out _));
            using var doc2 = JsonDocument.Parse(handler.Requests[1].Body!);
            Assert.Equal(454, doc2.RootElement.GetProperty("pcId").GetInt64());
        }

        [Fact]
        public async Task DeleteAwbUsesDeleteMethod()
        {
            var (client, handler) = Create();
            handler.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

            await client.DeleteAwbAsync(94348148);

            Assert.Equal(HttpMethod.Delete, handler.Requests[0].Method);
            Assert.Equal(Base + "/v1/awb/94348148", handler.Requests[0].Uri.ToString());
        }

        [Fact]
        public async Task NetworkErrorOnPostIsWrapped()
        {
            var (client, handler) = Create();
            handler.Enqueue(_ => throw new HttpRequestException("connection refused"));

            var ex = await Assert.ThrowsAsync<DragonStarException>(() => client.QuoteAsync(new QuoteRequest
            {
                ShipmentType = ShipmentType.Envelope, RecipientCounty = "Cluj", RecipientCity = "Dej",
            }));
            Assert.Equal(0, ex.Status);
            Assert.Equal("network_error", ex.Code);
            Assert.Single(handler.Requests);
        }

        [Fact]
        public void ShipmentStatusLabels()
        {
            Assert.Equal("Livrat", ShipmentStatus.Label(ShipmentStatus.Delivered));
            Assert.Equal("In centru destinatie", ShipmentStatus.Label(21));
            Assert.Equal("Spre livrare", ShipmentStatus.Label(18));
            Assert.True(ShipmentStatus.IsFinal(7));
            Assert.False(ShipmentStatus.IsFinal(6));
        }
    }
}
