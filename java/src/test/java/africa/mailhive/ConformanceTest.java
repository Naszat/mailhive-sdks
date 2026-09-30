package africa.mailhive;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertTrue;
import static org.junit.jupiter.api.Assertions.fail;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.node.ObjectNode;
import java.io.IOException;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.Iterator;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.regex.Pattern;
import java.util.stream.Stream;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Named;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.Arguments;
import org.junit.jupiter.params.provider.MethodSource;

/** The shared conformance suite (spec/conformance.json): every Mailhive SDK runs these same cases. */
class ConformanceTest {
    private static final Map<String, Class<? extends ApiException>> KINDS = Map.of(
            "api", ApiException.class,
            "authentication", AuthenticationException.class,
            "billing", BillingException.class,
            "permission", PermissionException.class,
            "not_found", NotFoundException.class,
            "conflict", ConflictException.class,
            "validation", ValidationException.class,
            "rate_limit", RateLimitException.class);

    private static JsonNode spec;
    private static MockServer mock;

    @BeforeAll
    static void start() throws IOException {
        spec = MockServer.spec("conformance.json");
        mock = new MockServer();
    }

    @AfterAll
    static void stop() throws InterruptedException {
        if (mock != null) {
            mock.close();
        }
    }

    static Stream<Arguments> cases() throws IOException {
        List<Arguments> cases = new ArrayList<>();
        for (JsonNode c : MockServer.spec("conformance.json").get("cases")) {
            cases.add(Arguments.of(Named.of(c.get("name").asText(), c)));
        }
        return cases.stream();
    }

    private static SendEmailParams email(JsonNode json) {
        SendEmailParams.Builder b = SendEmailParams.builder();
        Iterator<Map.Entry<String, JsonNode>> fields = json.fields();
        while (fields.hasNext()) {
            Map.Entry<String, JsonNode> f = fields.next();
            String v = f.getValue().asText();
            switch (f.getKey()) {
                case "from": b.from(v); break;
                case "to": b.to(v); break;
                case "cc": b.cc(v); break;
                case "bcc": b.bcc(v); break;
                case "reply_to": b.replyTo(v); break;
                case "subject": b.subject(v); break;
                case "html": b.html(v); break;
                case "text": b.text(v); break;
                case "template_id": b.templateId(v); break;
                default: fail("The test doesn't map email field " + f.getKey());
            }
        }
        return b.build();
    }

    private static Object run(JsonNode c) {
        Mailhive client = Mailhive.builder()
                .apiKey(c.get("key").asText())
                .baseUrl(mock.url + "/v1")
                .maxRetries(2)
                .build();
        SendEmailParams email = email(spec.get("email"));
        RequestOptions options = c.hasNonNull("idempotencyKey")
                ? RequestOptions.idempotencyKey(c.get("idempotencyKey").asText())
                : RequestOptions.none();
        switch (c.get("call").asText()) {
            case "emails.send":
                return client.emails().send(email, options);
            case "emails.sendBatch":
                return client.emails().sendBatch(List.of(email, email), options);
            case "emails.get":
                return client.emails().get(c.get("id").asText());
            default:
                throw new IllegalArgumentException(c.get("call").asText());
        }
    }

    @ParameterizedTest(name = "{0}")
    @MethodSource("cases")
    void conformance(JsonNode c) throws Exception {
        mock.reset();
        JsonNode want = c.get("expect");
        long started = System.nanoTime();
        Object result = null;
        MailhiveException error = null;
        try {
            result = run(c);
        } catch (MailhiveException caught) {
            error = caught;
        }
        double elapsedMs = (System.nanoTime() - started) / 1e6;
        JsonNode requests = mock.requests();

        assertEquals(want.get("attempts").asInt(), requests.size(), "attempts");
        for (String field : new String[] {"result", "resultFields"}) {
            if (want.has(field)) {
                JsonNode expected = want.get(field);
                JsonNode actual = Json.MAPPER.valueToTree(result);
                if (expected.isArray()) {
                    assertEquals(expected.size(), actual.size(), field + " length");
                    for (int i = 0; i < expected.size(); i++) {
                        assertEquals(expected.get(i), subset(actual.get(i), expected.get(i)), field + "[" + i + "]");
                    }
                } else {
                    assertEquals(expected, subset(actual, expected), field);
                }
            }
        }
        if (want.has("minElapsedMs")) {
            assertTrue(elapsedMs >= want.get("minElapsedMs").asDouble(), "elapsed " + elapsedMs + "ms");
        }
        if (want.path("sameIdempotencyKey").asBoolean(false)) {
            Set<String> keys = new HashSet<>();
            for (JsonNode r : requests) {
                keys.add(r.get("headers").hasNonNull("idempotency-key") ? r.get("headers").get("idempotency-key").asText() : null);
            }
            assertEquals(1, keys.size(), "one idempotency key: " + keys);
            assertFalse(keys.contains(null), "idempotency key present");
        }
        if (want.has("error")) {
            JsonNode expected = want.get("error");
            assertNotNull(error, "expected an error");
            Class<? extends ApiException> kind = KINDS.get(expected.get("kind").asText());
            assertTrue(kind.isInstance(error), "expected " + kind.getSimpleName() + ", got " + error);
            ApiException api = (ApiException) error;
            assertEquals(expected.get("status").asInt(), api.status());
            assertEquals(expected.get("code").asText(), api.code());
            if (expected.has("requestId")) {
                assertEquals(expected.get("requestId").asText(), api.requestId());
            }
            if (expected.path("hasDetails").asBoolean(false)) {
                assertTrue(api.details() != null && api.details().size() > 0, "details");
            }
        } else {
            assertNull(error, "unexpected error: " + error);
        }

        JsonNode request = want.get("request");
        if (request != null) {
            JsonNode first = requests.get(0);
            JsonNode headers = first.get("headers");
            Iterator<Map.Entry<String, JsonNode>> checks = request.fields();
            while (checks.hasNext()) {
                Map.Entry<String, JsonNode> check = checks.next();
                JsonNode v = check.getValue();
                String message = check.getKey() + " " + v + " vs " + first;
                switch (check.getKey()) {
                    case "method": assertEquals(v.asText(), first.get("method").asText(), message); break;
                    case "path": assertEquals(v.asText(), first.get("path").asText(), message); break;
                    case "authorization": assertEquals(v.asText(), headers.path("authorization").asText(), message); break;
                    case "contentType": assertEquals(v.asText(), headers.path("content-type").asText(), message); break;
                    case "userAgentPattern":
                        assertTrue(Pattern.compile(v.asText()).matcher(headers.path("user-agent").asText()).find(), message);
                        break;
                    case "idempotencyKeyPattern":
                        assertTrue(Pattern.compile(v.asText()).matcher(headers.path("idempotency-key").asText()).find(), message);
                        break;
                    case "idempotencyKey": assertEquals(v.asText(), headers.path("idempotency-key").asText(), message); break;
                    case "noIdempotencyKey": assertFalse(headers.has("idempotency-key"), message); break;
                    case "body": assertEquals(v, first.get("body"), message); break;
                    case "bodyEmailCount": assertEquals(v.asInt(), first.get("body").get("emails").size(), message); break;
                    default: fail("Unknown request check " + check.getKey());
                }
            }
        }
    }

    private static JsonNode subset(JsonNode actual, JsonNode keys) {
        ObjectNode picked = Json.MAPPER.createObjectNode();
        keys.fieldNames().forEachRemaining(k -> picked.set(k, actual.get(k)));
        return picked;
    }
}
