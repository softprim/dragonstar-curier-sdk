package ro.softprim.dragonstar;

import com.sun.net.httpserver.HttpServer;

import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.InetAddress;
import java.net.InetSocketAddress;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.concurrent.ConcurrentLinkedQueue;
import java.util.function.Function;

/** Local fake API server with scripted responses; records every request it receives. */
final class FakeServer implements AutoCloseable {

    static final class Recorded {
        final String method;
        final String rawPath;
        final String rawQuery;
        final Map<String, String> headers;
        final byte[] body;

        Recorded(String method, String rawPath, String rawQuery, Map<String, String> headers, byte[] body) {
            this.method = method;
            this.rawPath = rawPath;
            this.rawQuery = rawQuery;
            this.headers = headers;
            this.body = body;
        }

        String header(String name) {
            return headers.get(name.toLowerCase());
        }

        String bodyText() {
            return new String(body, StandardCharsets.UTF_8);
        }

        @SuppressWarnings("unchecked")
        Map<String, Object> json() {
            return (Map<String, Object>) Json.parse(bodyText());
        }
    }

    static final class Reply {
        final int status;
        final Map<String, String> headers = new LinkedHashMap<>();
        final byte[] body;

        Reply(int status, byte[] body) {
            this.status = status;
            this.body = body;
        }

        Reply header(String name, String value) {
            headers.put(name, value);
            return this;
        }
    }

    static Reply json(int status, String json) {
        Reply r = new Reply(status, json.getBytes(StandardCharsets.UTF_8));
        return r.header("Content-Type", status >= 400 ? "application/problem+json" : "application/json");
    }

    static Reply bytes(int status, byte[] body, String contentType) {
        return new Reply(status, body).header("Content-Type", contentType);
    }

    static Reply empty(int status) {
        return new Reply(status, new byte[0]);
    }

    private final HttpServer server;
    private final ConcurrentLinkedQueue<Function<Recorded, Reply>> script = new ConcurrentLinkedQueue<>();
    final List<Recorded> requests = Collections.synchronizedList(new ArrayList<>());

    FakeServer() throws IOException {
        server = HttpServer.create(new InetSocketAddress(InetAddress.getLoopbackAddress(), 0), 0);
        server.createContext("/", exchange -> {
            try (InputStream in = exchange.getRequestBody()) {
                byte[] body = in.readAllBytes();
                Map<String, String> headers = new LinkedHashMap<>();
                exchange.getRequestHeaders().forEach((k, v) -> headers.put(k.toLowerCase(), String.join(",", v)));
                Recorded rec = new Recorded(exchange.getRequestMethod(), exchange.getRequestURI().getRawPath(),
                        exchange.getRequestURI().getRawQuery(), headers, body);
                requests.add(rec);
                Function<Recorded, Reply> next = script.poll();
                Reply reply = next == null
                        ? json(500, "{\"title\":\"No scripted response\",\"status\":500}")
                        : next.apply(rec);
                reply.headers.forEach((k, v) -> exchange.getResponseHeaders().add(k, v));
                if (reply.body.length == 0) {
                    exchange.sendResponseHeaders(reply.status, -1);
                } else {
                    exchange.sendResponseHeaders(reply.status, reply.body.length);
                    try (OutputStream out = exchange.getResponseBody()) {
                        out.write(reply.body);
                    }
                }
            } finally {
                exchange.close();
            }
        });
        server.start();
    }

    FakeServer enqueue(Reply reply) {
        script.add(r -> reply);
        return this;
    }

    FakeServer enqueue(Function<Recorded, Reply> responder) {
        script.add(responder);
        return this;
    }

    String url() {
        return "http://127.0.0.1:" + server.getAddress().getPort();
    }

    DragonStarClient client() {
        return DragonStarClient.builder("test-key").baseUrl(url() + "/").build();
    }

    @Override
    public void close() {
        server.stop(0);
    }
}
