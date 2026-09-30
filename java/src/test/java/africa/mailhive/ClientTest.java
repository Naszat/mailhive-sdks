package africa.mailhive;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.fasterxml.jackson.databind.JsonNode;
import com.sun.net.httpserver.HttpExchange;
import com.sun.net.httpserver.HttpServer;
import java.io.IOException;
import java.io.OutputStream;
import java.net.InetAddress;
import java.net.InetSocketAddress;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.util.ArrayList;
import java.util.Base64;
import java.util.Collections;
import java.util.List;
import java.util.Map;
import java.util.concurrent.Executors;
import java.util.concurrent.atomic.AtomicInteger;
import org.junit.jupiter.api.AfterEach;
import org.junit.jupiter.api.Test;

class ClientTest {
    private HttpServer server;
    private final AtomicInteger hits = new AtomicInteger();
    private final List<Double> waits = Collections.synchronizedList(new ArrayList<>());

    interface Handler {
        void handle(HttpExchange exchange) throws Exception;
    }

    private String serve(Handler handler) throws IOException {
        server = HttpServer.create(new InetSocketAddress(InetAddress.getLoopbackAddress(), 0), 0);
        server.setExecutor(Executors.newCachedThreadPool());
        server.createContext("/", exchange -> {
            hits.incrementAndGet();
            try {
                handler.handle(exchange);
            } catch (Exception ignored) {
                // the client may have hung up
            } finally {
                exchange.close();
            }
        });
        server.start();
        return "http://127.0.0.1:" + server.getAddress().getPort() + "/v1";
    }

    @AfterEach
    void stop() {
        if (server != null) {
            server.stop(0);
        }
    }

    private static void respond(HttpExchange exchange, int status, String body, String... headers) throws IOException {
        for (int i = 0; i < headers.length; i += 2) {
            exchange.getResponseHeaders().add(headers[i], headers[i + 1]);
        }
        exchange.getResponseHeaders().add("Content-Type", "application/json");
        byte[] bytes = body.getBytes(StandardCharsets.UTF_8);
        exchange.sendResponseHeaders(status, bytes.length);
        try (OutputStream out = exchange.getResponseBody()) {
            out.write(bytes);
        }
    }

    private Mailhive.Builder client(String baseUrl) {
        return Mailhive.builder().apiKey("mhs_test_key").baseUrl(baseUrl).environment(name -> null)
                .sleeper(waits::add);
    }

    private static SendEmailParams simpleEmail() {
        return SendEmailParams.builder().from("hello@acme.com").to("ada@example.com").subject("Hi").text("Hello").build();
    }

    // --- configuration ---

    @Test
    void readsTheKeyAndBaseUrlFromTheEnvironment() {
        Map<String, String> env = Map.of("MAILHIVE_API_KEY", "mhs_from_env", "MAILHIVE_BASE_URL", "https://api-beta.mailhive.africa/v1//");
        Mailhive client = Mailhive.builder().environment(env::get).build();
        assertEquals("https://api-beta.mailhive.africa/v1", client.baseUrl());
    }

    @Test
    void defaults() {
        Mailhive client = Mailhive.builder().environment(Map.of("MAILHIVE_API_KEY", "mhs_x")::get).build();
        assertEquals("https://api.mailhive.africa/v1", client.baseUrl());
        assertEquals(Duration.ofSeconds(30), client.timeout());
        assertEquals(2, client.maxRetries());
        assertEquals(0, Mailhive.builder().apiKey("mhs_x").maxRetries(-3).build().maxRetries());
    }

    @Test
    void anExplicitKeyWinsOverTheEnvironment() throws IOException {
        List<String> auth = new ArrayList<>();
        String url = serve(exchange -> {
            auth.add(exchange.getRequestHeaders().getFirst("Authorization"));
            respond(exchange, 200, "{\"id\":\"msg_1\",\"status\":\"queued\",\"suppressed\":[],\"test\":true}");
        });
        Mailhive client = Mailhive.builder().apiKey("mhs_explicit").baseUrl(url)
                .environment(Map.of("MAILHIVE_API_KEY", "mhs_env")::get).build();
        assertTrue(client.emails().send(simpleEmail()).test());
        assertEquals(List.of("Bearer mhs_explicit"), auth);
    }

    @Test
    void refusesToStartWithoutAKey() {
        MailhiveException error = assertThrows(MailhiveException.class,
                () -> Mailhive.builder().environment(name -> null).build());
        assertTrue(error.getMessage().contains("MAILHIVE_API_KEY"));
        assertThrows(MailhiveException.class, () -> Mailhive.builder().apiKey("").environment(name -> "").build());
    }

    @Test
    void refusesAPublishableKey() {
        MailhiveException error = assertThrows(MailhiveException.class,
                () -> Mailhive.builder().apiKey("mhp_abc").environment(name -> null).build());
        assertEquals("That's a form's publishable key (mhp_…). The server SDK needs a secret API key (mhs_…) "
                + "from Mailhive Send → API keys.", error.getMessage());
    }

    @Test
    void neverShowsTheKey() {
        Mailhive client = Mailhive.builder().apiKey("mhs_live_supersecret").environment(name -> null).build();
        assertFalse(client.toString().contains("supersecret"));
        assertFalse(Mailhive.builder().apiKey("mhs_live_supersecret").toString().contains("supersecret"));
    }

    @Test
    void versionIsXYZ() {
        assertTrue(Mailhive.VERSION.matches("^\\d+\\.\\d+\\.\\d+$"), Mailhive.VERSION);
        assertFalse(Mailhive.VERSION.equals("0.0.0"), "the version resource wasn't filtered");
        assertEquals("0.1.0", Version.clean("0.1.0-SNAPSHOT"));
        assertEquals("1.2.3", Version.clean("1.2.3"));
        assertEquals("0.0.0", Version.clean("${project.version}"));
    }

    // --- request bodies ---

    @Test
    void encodesTheBodyWithTheApiFieldNames() {
        byte[] pdf = {0x25, 0x50, 0x44, 0x46, (byte) 0xff};
        SendEmailParams email = SendEmailParams.builder()
                .from("Acme <hello@acme.com>")
                .to("ada@example.com", "grace@example.com")
                .cc("cc@example.com")
                .bcc(List.of())
                .replyTo("support@acme.com")
                .subject("Your receipt")
                .templateId("tmpl_1")
                .variable("name", "Ada").variable("total", 42)
                .header("X-Order", "1042")
                .tag("type", "receipt")
                .attachment(Attachment.of("invoice.pdf", pdf, "application/pdf"))
                .attachment(Attachment.ofBase64("note.txt", "aGk="))
                .build();
        JsonNode json = email.toJson(Json.MAPPER);
        assertEquals("Acme <hello@acme.com>", json.get("from").asText());
        assertTrue(json.get("to").isArray());
        assertEquals(2, json.get("to").size());
        assertEquals("cc@example.com", json.get("cc").asText());
        assertFalse(json.has("bcc"));
        assertFalse(json.has("html"));
        assertFalse(json.has("text"));
        assertEquals("support@acme.com", json.get("reply_to").asText());
        assertEquals("tmpl_1", json.get("template_id").asText());
        assertEquals(42, json.get("variables").get("total").asInt());
        assertEquals("1042", json.get("headers").get("X-Order").asText());
        assertEquals("receipt", json.get("tags").get("type").asText());
        JsonNode first = json.get("attachments").get(0);
        assertEquals("invoice.pdf", first.get("filename").asText());
        assertEquals(Base64.getEncoder().encodeToString(pdf), first.get("content").asText());
        assertEquals("application/pdf", first.get("content_type").asText());
        JsonNode second = json.get("attachments").get(1);
        assertEquals("aGk=", second.get("content").asText());
        assertFalse(second.has("content_type"));
    }

    @Test
    void omitsEmptyCollections() {
        JsonNode json = SendEmailParams.builder().from("a@b.co").to("c@d.co").build().toJson(Json.MAPPER);
        assertEquals(2, json.size());
    }

    // --- transport ---

    @Test
    void retryAfterOverAMinuteGivesUpAfterOneRequest() throws IOException {
        String url = serve(exchange -> respond(exchange, 429,
                "{\"error\":{\"code\":\"rate_limited\",\"message\":\"Slow down\",\"details\":null,\"request_id\":\"req_1\"}}",
                "Retry-After", "120"));
        RateLimitException error = assertThrows(RateLimitException.class,
                () -> client(url).build().emails().send(simpleEmail()));
        assertEquals(1, hits.get());
        assertEquals("rate_limited", error.code());
        assertEquals(120.0, error.retryAfter().orElseThrow());
        assertEquals("req_1", error.requestId());
        assertNull(error.details());
        assertTrue(waits.isEmpty());
    }

    @Test
    void timesOutAndRetriesThenThrowsAConnectionException() throws IOException {
        String url = serve(exchange -> {
            Thread.sleep(2000);
            respond(exchange, 200, "{}");
        });
        ConnectionException error = assertThrows(ConnectionException.class,
                () -> client(url).timeout(Duration.ofMillis(200)).build().emails().send(simpleEmail()));
        assertEquals("The Mailhive API didn't answer within 0.2 seconds.", error.getMessage());
        assertEquals(3, hits.get());
        assertEquals(2, waits.size());
    }

    @Test
    void anUnreachableServerIsAConnectionException() throws IOException {
        String url = serve(exchange -> respond(exchange, 200, "{}"));
        server.stop(0);
        server = null;
        ConnectionException error = assertThrows(ConnectionException.class,
                () -> client(url).maxRetries(0).build().emails().send(simpleEmail()));
        assertEquals("Couldn't reach the Mailhive API at " + url + ".", error.getMessage());
    }

    @Test
    void backsOffWithJitterWithoutRetryAfter() throws IOException {
        String url = serve(exchange -> respond(exchange, 503,
                "{\"error\":{\"code\":\"maintenance\",\"message\":\"Down\",\"details\":null,\"request_id\":\"req_1\"}}"));
        assertThrows(ApiException.class, () -> client(url).build().emails().send(simpleEmail()));
        assertEquals(3, hits.get());
        assertEquals(2, waits.size());
        assertTrue(waits.get(0) >= 0.25 && waits.get(0) <= 0.5, "first wait " + waits.get(0));
        assertTrue(waits.get(1) >= 0.5 && waits.get(1) <= 1.0, "second wait " + waits.get(1));
    }

    @Test
    void aNonJsonErrorIsAnHttpError() throws IOException {
        String url = serve(exchange -> {
            exchange.getResponseHeaders().add("X-Request-Id", "req_header");
            byte[] bytes = "<h1>Bad request</h1>".getBytes(StandardCharsets.UTF_8);
            exchange.sendResponseHeaders(400, bytes.length);
            exchange.getResponseBody().write(bytes);
        });
        ValidationException error = assertThrows(ValidationException.class,
                () -> client(url).build().emails().send(simpleEmail()));
        assertEquals("http_error", error.code());
        assertEquals("HTTP 400", error.getMessage());
        assertEquals("req_header", error.requestId());
        assertEquals(400, error.status());
    }

    @Test
    void encodesTheEmailIdInThePath() throws IOException {
        List<String> paths = new ArrayList<>();
        String url = serve(exchange -> {
            paths.add(exchange.getRequestURI().getRawPath());
            respond(exchange, 200, "{\"id\":\"a b/c\",\"status\":\"sent\",\"brand_new_field\":1}");
        });
        Email email = client(url).build().emails().get("a b/c");
        assertEquals(List.of("/v1/send/emails/a%20b%2Fc"), paths);
        assertEquals("sent", email.status());
        assertEquals(1, email.other().get("brand_new_field"));
    }
}
