package ro.softprim.dragonstar;

import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

import java.io.IOException;
import java.time.Duration;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

import static org.junit.jupiter.api.Assertions.assertArrayEquals;
import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

class DragonStarClientTest {

    private FakeServer server;
    private DragonStarClient client;

    @BeforeEach
    void setUp() throws IOException {
        server = new FakeServer();
        client = server.client();
    }

    @AfterEach
    void tearDown() {
        server.close();
    }

    private static Map<String, Object> awbData() {
        Map<String, Object> d = new LinkedHashMap<>();
        d.put("shipmentType", "parcel");
        d.put("weight", 2.5);
        d.put("pieces", 1);
        d.put("recipientName", "Ion \"Nelu\" Popescu");
        d.put("recipientAddress", "Str. Exemplu 1");
        d.put("recipientCounty", "Cluj");
        d.put("recipientCity", "Cluj-Napoca");
        d.put("codAmount", 100);
        d.put("returnAwb", true);
        return d;
    }

    // 1. Authorization + User-Agent
    @Test
    void sendsAuthorizationAndUserAgentHeaders() {
        server.enqueue(FakeServer.json(200, "{\"masterId\":454,\"count\":0,\"data\":[]}"));
        Map<String, Object> res = client.getBranches();
        assertEquals(454L, res.get("masterId"));

        FakeServer.Recorded r = server.requests.get(0);
        assertEquals("GET", r.method);
        assertEquals("/v1/branches", r.rawPath);
        assertEquals("Bearer test-key", r.header("Authorization"));
        assertEquals("softprim-dragonstar-java/1.0.0", r.header("User-Agent"));
        assertTrue(r.header("Accept").startsWith("application/json"));
        assertNull(r.header("Content-Type"));
    }

    @Test
    void customUserAgent() {
        DragonStarClient c = DragonStarClient.builder("k").baseUrl(server.url()).userAgent("my-shop/2.0").build();
        server.enqueue(FakeServer.json(200, "{\"masterId\":1,\"count\":0,\"data\":[]}"));
        c.getBranches();
        assertEquals("my-shop/2.0", server.requests.get(0).header("User-Agent"));
        assertEquals("Bearer k", server.requests.get(0).header("Authorization"));
    }

    // 2. createAwb sends correct JSON and returns the AWB
    @Test
    void createAwbSendsJsonAndReturnsAwb() {
        server.enqueue(FakeServer.json(201, "{\"awb\":94348148,\"totalAmount\":9.83,\"weight\":2.5,"
                + "\"pieces\":1,\"codAmount\":100,\"codPaymentType\":\"account\"}"));

        Map<String, Object> res = client.createAwb(awbData());

        assertEquals(94348148L, res.get("awb"));
        assertEquals(9.83, res.get("totalAmount"));
        assertEquals("account", res.get("codPaymentType"));

        FakeServer.Recorded r = server.requests.get(0);
        assertEquals("POST", r.method);
        assertEquals("/v1/awb", r.rawPath);
        assertEquals("application/json", r.header("Content-Type"));
        Map<String, Object> sent = r.json();
        assertEquals("parcel", sent.get("shipmentType"));
        assertEquals(2.5, sent.get("weight"));
        assertEquals(1L, sent.get("pieces"));
        assertEquals("Ion \"Nelu\" Popescu", sent.get("recipientName"));
        assertEquals(Boolean.TRUE, sent.get("returnAwb"));
        assertEquals(awbData().keySet(), sent.keySet());
    }

    @Test
    void createAwbForBranchAddsPcId() {
        server.enqueue(FakeServer.json(201, "{\"awb\":1}"));
        client.createAwbForBranch(454, awbData());
        FakeServer.Recorded r = server.requests.get(0);
        assertEquals("/v1/awb/branch", r.rawPath);
        assertEquals(454L, r.json().get("pcId"));
        assertEquals("Cluj", r.json().get("recipientCounty"));
    }

    @Test
    void quoteEndpoints() {
        server.enqueue(FakeServer.json(200, "{\"netAmount\":21,\"vatAmount\":4.41,\"totalAmount\":25.41,\"isListRate\":true}"));
        server.enqueue(FakeServer.json(200, "{\"totalAmount\":10}"));
        Map<String, Object> q = client.quote(awbData());
        assertEquals(25.41, q.get("totalAmount"));
        assertEquals(Boolean.TRUE, q.get("isListRate"));
        client.quoteForBranch(7, awbData());
        assertEquals("/v1/awb/quote", server.requests.get(0).rawPath);
        assertEquals("/v1/awb/quote/branch", server.requests.get(1).rawPath);
        assertEquals(7L, server.requests.get(1).json().get("pcId"));
    }

    // 3. 422 -> DragonStarException with validation_failed + errors
    @Test
    void validationErrorBecomesException() {
        server.enqueue(FakeServer.json(422, "{\"type\":\"https://api.curierdragonstar.ro/errors/validation-failed\","
                + "\"title\":\"Validation failed\",\"status\":422,\"code\":\"validation_failed\","
                + "\"detail\":\"The data sent is not valid.\","
                + "\"errors\":{\"weight\":[\"required if shipmentType = parcel or pallet\"],"
                + "\"recipientCity\":[\"unknown\",\"too short\"]}}"));

        DragonStarException e = assertThrows(DragonStarException.class, () -> client.createAwb(awbData()));
        assertEquals(422, e.getStatus());
        assertEquals("validation_failed", e.getCode());
        assertEquals("Validation failed", e.getTitle());
        assertEquals("The data sent is not valid.", e.getDetail());
        assertEquals("The data sent is not valid.", e.getMessage());
        assertEquals("https://api.curierdragonstar.ro/errors/validation-failed", e.getType());
        assertEquals(List.of("required if shipmentType = parcel or pallet"), e.getErrors().get("weight"));
        assertEquals(List.of("unknown", "too short"), e.getErrors().get("recipientCity"));
        assertNull(e.getRetryAfter());
        assertEquals(1, server.requests.size());
    }

    @Test
    void errorMessageFallsBackToTitleThenStatus() {
        server.enqueue(FakeServer.json(404, "{\"title\":\"Resource not found\",\"status\":404,\"code\":\"awb_not_found\"}"));
        server.enqueue(FakeServer.bytes(500, "<html>oops</html>".getBytes(), "text/html"));
        DragonStarException e1 = assertThrows(DragonStarException.class, () -> client.track("1"));
        assertEquals("Resource not found", e1.getMessage());
        assertEquals("awb_not_found", e1.getCode());
        assertTrue(e1.getErrors().isEmpty());
        DragonStarException e2 = assertThrows(DragonStarException.class, () -> client.track("1"));
        assertEquals("HTTP 500", e2.getMessage());
        assertEquals(500, e2.getStatus());
        assertNull(e2.getCode());
    }

    // 4. 429 with Retry-After: 0 is retried and succeeds
    @Test
    void rateLimitIsRetried() {
        server.enqueue(FakeServer.json(429, "{\"title\":\"Too many requests\",\"status\":429,\"code\":\"rate_limit_exceeded\"}")
                .header("Retry-After", "0"));
        server.enqueue(FakeServer.json(201, "{\"awb\":555}"));

        Map<String, Object> res = client.createAwb(awbData());
        assertEquals(555L, res.get("awb"));
        assertEquals(2, server.requests.size());
        assertEquals(server.requests.get(0).bodyText(), server.requests.get(1).bodyText());
    }

    @Test
    void rateLimitGivesUpAfterMaxRetries() {
        for (int i = 0; i < 3; i++) {
            server.enqueue(FakeServer.json(429, "{\"title\":\"Too many requests\",\"status\":429,\"code\":\"rate_limit_exceeded\"}")
                    .header("Retry-After", "0"));
        }
        DragonStarException e = assertThrows(DragonStarException.class, () -> client.getBranches());
        assertEquals(429, e.getStatus());
        assertEquals(Integer.valueOf(0), e.getRetryAfter());
        assertEquals(3, server.requests.size()); // 1 + maxRetries(2)
    }

    @Test
    void temporarilyBannedIsNotRetried() {
        server.enqueue(FakeServer.json(429, "{\"title\":\"Banned\",\"status\":429,\"code\":\"temporarily_banned\"}")
                .header("Retry-After", "0"));
        DragonStarException e = assertThrows(DragonStarException.class, () -> client.getCounties());
        assertEquals("temporarily_banned", e.getCode());
        assertEquals(1, server.requests.size());
    }

    // 5. POST receiving 503 is NOT retried
    @Test
    void postWith503IsNotRetried() {
        server.enqueue(FakeServer.json(503, "{\"title\":\"Unavailable\",\"status\":503,\"code\":\"service_unavailable\"}")
                .header("Retry-After", "0"));
        server.enqueue(FakeServer.json(201, "{\"awb\":1}"));

        DragonStarException e = assertThrows(DragonStarException.class, () -> client.createAwb(awbData()));
        assertEquals(503, e.getStatus());
        assertEquals("service_unavailable", e.getCode());
        assertEquals(1, server.requests.size());
    }

    @Test
    void getWith503IsRetried() {
        server.enqueue(FakeServer.json(503, "{\"title\":\"Unavailable\",\"status\":503,\"code\":\"service_unavailable\"}")
                .header("Retry-After", "0"));
        server.enqueue(FakeServer.json(200, "{\"count\":1,\"data\":{\"CJ\":\"Cluj\"}}"));
        assertEquals("Cluj", client.getCounties().get("CJ"));
        assertEquals(2, server.requests.size());
    }

    @Test
    void maxRetriesZeroDisablesRetries() {
        DragonStarClient c = DragonStarClient.builder("k").baseUrl(server.url()).maxRetries(0).build();
        server.enqueue(FakeServer.json(429, "{\"status\":429,\"code\":\"rate_limit_exceeded\"}").header("Retry-After", "0"));
        assertThrows(DragonStarException.class, c::getBranches);
        assertEquals(1, server.requests.size());
    }

    // 6. trackMany with 150 AWBs -> 2 requests, merged data + notFound
    @Test
    void trackManySplitsIntoBatchesOf100() {
        List<Long> awbs = new ArrayList<>();
        for (long i = 0; i < 150; i++) {
            awbs.add(94000000L + i);
        }
        server.enqueue(rec -> {
            List<?> sent = (List<?>) rec.json().get("awbs");
            StringBuilder data = new StringBuilder();
            for (int i = 1; i < sent.size(); i++) { // first AWB of the batch is "not found"
                if (data.length() > 0) {
                    data.append(',');
                }
                data.append("{\"awb\":").append(sent.get(i)).append(",\"status\":{\"code\":6}}");
            }
            return FakeServer.json(200, "{\"count\":" + (sent.size() - 1) + ",\"data\":[" + data
                    + "],\"notFound\":[" + sent.get(0) + "]}");
        });
        server.enqueue(rec -> {
            List<?> sent = (List<?>) rec.json().get("awbs");
            StringBuilder data = new StringBuilder();
            for (int i = 0; i < sent.size() - 1; i++) { // last AWB of the batch is "not found"
                if (data.length() > 0) {
                    data.append(',');
                }
                data.append("{\"awb\":").append(sent.get(i)).append(",\"status\":{\"code\":3}}");
            }
            return FakeServer.json(200, "{\"count\":" + (sent.size() - 1) + ",\"data\":[" + data
                    + "],\"notFound\":[" + sent.get(sent.size() - 1) + "]}");
        });

        Map<String, Object> res = client.trackMany(awbs, true);

        assertEquals(2, server.requests.size());
        assertEquals("/v1/awb/tracking", server.requests.get(0).rawPath);
        assertEquals("events=true", server.requests.get(0).rawQuery);
        assertEquals(100, ((List<?>) server.requests.get(0).json().get("awbs")).size());
        assertEquals(50, ((List<?>) server.requests.get(1).json().get("awbs")).size());
        assertEquals(94000100L, ((List<?>) server.requests.get(1).json().get("awbs")).get(0));

        List<?> data = (List<?>) res.get("data");
        assertEquals(148, data.size());
        assertEquals(148L, res.get("count"));
        assertEquals(List.of(94000000L, 94000149L), res.get("notFound"));
        assertEquals(94000001L, ((Map<?, ?>) data.get(0)).get("awb"));
    }

    @Test
    void trackManyDefaultsToNoEvents() {
        server.enqueue(FakeServer.json(200, "{\"count\":0,\"data\":[],\"notFound\":[1]}"));
        Map<String, Object> res = client.trackMany(List.of(1L));
        assertEquals("events=false", server.requests.get(0).rawQuery);
        assertEquals(List.of(1L), res.get("notFound"));
    }

    @Test
    void trackSendsEventsFlag() {
        server.enqueue(FakeServer.json(200, "{\"status\":{\"code\":3,\"name\":\"Livrat\"},\"events\":[]}"));
        server.enqueue(FakeServer.json(200, "{\"status\":{\"code\":6}}"));
        Map<String, Object> t = client.track(94348148L);
        client.track("94348148", false);
        assertEquals("/v1/awb/94348148", server.requests.get(0).rawPath);
        assertEquals("events=true", server.requests.get(0).rawQuery);
        assertEquals("events=false", server.requests.get(1).rawQuery);
        assertEquals(3L, ((Map<?, ?>) t.get("status")).get("code"));
    }

    // 7. getLabel returns bytes unchanged and refuses "a4"
    @Test
    void getLabelReturnsRawBytes() {
        byte[] pdf = new byte[256];
        for (int i = 0; i < pdf.length; i++) {
            pdf[i] = (byte) i;
        }
        server.enqueue(FakeServer.bytes(200, pdf, "application/pdf"));
        server.enqueue(FakeServer.bytes(200, pdf, "application/pdf"));

        assertArrayEquals(pdf, client.getLabel("94348148", "A6"));
        assertArrayEquals(pdf, client.getLabel(94348148L));
        assertEquals("/v1/awb/94348148/label/A6", server.requests.get(0).rawPath);
        assertEquals("/v1/awb/94348148/label", server.requests.get(1).rawPath);
    }

    @Test
    void getLabelRejectsInvalidFormatLocally() {
        assertThrows(IllegalArgumentException.class, () -> client.getLabel("94348148", "a4"));
        assertThrows(IllegalArgumentException.class, () -> client.getLabel("94348148", "A5"));
        assertEquals(0, server.requests.size());
    }

    @Test
    void getLabelZpl() {
        server.enqueue(FakeServer.bytes(200, "^XA^FDȘoseaua^FS^XZ".getBytes(java.nio.charset.StandardCharsets.UTF_8), "text/plain"));
        assertEquals("^XA^FDȘoseaua^FS^XZ", client.getLabelZpl("94348148", 2));
        assertEquals("/v1/awb/94348148/label/zpl/2", server.requests.get(0).rawPath);
        assertThrows(IllegalArgumentException.class, () -> client.getLabelZpl("94348148", 0));
        assertEquals(1, server.requests.size());
    }

    // 8. getLocalitiesWithKm parses "Cluj-Napoca:450"
    @Test
    void localitiesWithKmAreParsed() {
        server.enqueue(FakeServer.json(200, "{\"count\":3,\"data\":[\"Cluj-Napoca:450\",\"Sat: Valea:12.5\",\"Fara\"]}"));
        List<LocalityKm> list = client.getLocalitiesWithKm("Cluj");
        assertEquals("/v1/counties/Cluj/localities-km", server.requests.get(0).rawPath);
        assertEquals(3, list.size());
        assertEquals("Cluj-Napoca", list.get(0).name());
        assertEquals(Double.valueOf(450.0), list.get(0).km());
        assertEquals(new LocalityKm("Cluj-Napoca", 450.0), list.get(0));
        assertEquals("Sat: Valea", list.get(1).name()); // split at the LAST colon
        assertEquals(Double.valueOf(12.5), list.get(1).km());
        assertEquals("Fara", list.get(2).name());
        assertNull(list.get(2).km());
    }

    // 9. county with diacritics / spaces is URL-encoded
    @Test
    void countyIsUrlEncoded() {
        server.enqueue(FakeServer.json(200, "{\"count\":2,\"data\":[\"Bistrița\",\"Năsăud\"]}"));
        List<String> res = client.getLocalities("Bistrița Năsăud");
        assertEquals("/v1/counties/Bistri%C8%9Ba%20N%C4%83s%C4%83ud/localities", server.requests.get(0).rawPath);
        assertEquals(List.of("Bistrița", "Năsăud"), res);
    }

    @Test
    void countiesMap() {
        server.enqueue(FakeServer.json(200, "{\"count\":2,\"data\":{\"B\":\"Bucuresti\",\"CJ\":\"Cluj\"}}"));
        Map<String, String> c = client.getCounties();
        assertEquals(List.of("B", "CJ"), new ArrayList<>(c.keySet()));
        assertEquals("Cluj", c.get("CJ"));
    }

    @Test
    void deleteAwbAndReturns() {
        server.enqueue(FakeServer.empty(204));
        server.enqueue(FakeServer.json(200, "{\"count\":0,\"data\":[]}"));
        client.deleteAwb(94348148L);
        Map<String, Object> ret = client.getReturns("94348148");
        assertEquals("DELETE", server.requests.get(0).method);
        assertEquals("/v1/awb/94348148", server.requests.get(0).rawPath);
        assertEquals("/v1/awb/94348148/return", server.requests.get(1).rawPath);
        assertEquals(0L, ret.get("count"));
    }

    @Test
    void pickupsAndManifests() {
        server.enqueue(FakeServer.json(201, "{\"pickupId\":321}"));
        server.enqueue(FakeServer.empty(201));
        server.enqueue(FakeServer.json(201, "{\"manifestId\":77,\"shipmentCount\":12}"));
        server.enqueue(FakeServer.empty(200));
        server.enqueue(FakeServer.bytes(200, new byte[]{'%', 'P', 'D', 'F'}, "application/pdf"));

        Map<String, Object> pickup = new LinkedHashMap<>();
        pickup.put("pickupDate", "2026-10-13");
        pickup.put("pickupWindowStart", 9);
        pickup.put("pickupWindowEnd", 17);
        pickup.put("weight", 10);
        pickup.put("parcels", 2);

        assertEquals(321L, client.createPickup(pickup).get("pickupId"));
        assertTrue(client.createPickupForBranch(454, pickup).isEmpty());
        assertEquals(454L, server.requests.get(1).json().get("pcId"));

        Map<String, Object> m = client.createManifest();
        assertEquals(77L, m.get("manifestId"));
        assertEquals(12L, m.get("shipmentCount"));
        assertEquals("", server.requests.get(2).bodyText());
        assertNull(server.requests.get(2).header("Content-Type"));

        Map<String, Object> none = client.createManifest();
        assertTrue(none.containsKey("manifestId"));
        assertNull(none.get("manifestId"));

        assertArrayEquals(new byte[]{'%', 'P', 'D', 'F'}, client.getManifestPdf(77));
        assertEquals("/v1/manifests/77/pdf", server.requests.get(4).rawPath);
    }

    @Test
    void networkErrorIsWrapped() throws IOException {
        FakeServer closed = new FakeServer();
        String url = closed.url();
        closed.close();
        DragonStarClient c = DragonStarClient.builder("k").baseUrl(url).maxRetries(0)
                .timeout(Duration.ofSeconds(5)).build();
        DragonStarException e = assertThrows(DragonStarException.class, c::getBranches);
        assertEquals(0, e.getStatus());
        assertEquals("network_error", e.getCode());
    }

    @Test
    void builderValidation() {
        assertThrows(IllegalArgumentException.class, () -> DragonStarClient.builder(""));
        assertThrows(IllegalArgumentException.class, () -> DragonStarClient.builder("k").maxRetries(-1));
        assertThrows(IllegalArgumentException.class, () -> DragonStarClient.builder("k").timeout(Duration.ZERO));
        DragonStarClient c = DragonStarClient.create("k");
        assertEquals("https://api.curierdragonstar.ro", c.getBaseUrl());
        assertEquals(Duration.ofSeconds(30), c.getTimeout());
        assertEquals(2, c.getMaxRetries());
        assertEquals("softprim-dragonstar-java/1.0.0", c.getUserAgent());
        assertEquals("https://x.test", DragonStarClient.builder("k").baseUrl("https://x.test//").build().getBaseUrl());
    }

    @Test
    void retryAfterParsing() {
        assertEquals(Integer.valueOf(5), DragonStarClient.parseRetryAfter("5"));
        assertNull(DragonStarClient.parseRetryAfter(null));
        assertNull(DragonStarClient.parseRetryAfter("soon"));
        assertEquals(Integer.valueOf(0), DragonStarClient.parseRetryAfter("Wed, 21 Oct 2015 07:28:00 GMT"));
    }

    @Test
    void shipmentStatus() {
        assertEquals("Livrat", ShipmentStatus.label(3));
        assertEquals("In centru destinatie", ShipmentStatus.label(21));
        assertNull(ShipmentStatus.label(99));
        assertEquals(ShipmentStatus.OUT_FOR_DELIVERY, ShipmentStatus.fromCode(18));
        assertNull(ShipmentStatus.fromCode(8));
        assertTrue(ShipmentStatus.isFinal(3));
        assertTrue(ShipmentStatus.isFinal(7));
        assertFalse(ShipmentStatus.isFinal(6));
        assertTrue(ShipmentStatus.RETURNED.isFinal());
        assertEquals(10, ShipmentStatus.values().length);
    }
}
