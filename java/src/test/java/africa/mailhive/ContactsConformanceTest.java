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
import java.util.Iterator;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.stream.Stream;
import org.junit.jupiter.api.AfterAll;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Named;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.Arguments;
import org.junit.jupiter.params.provider.MethodSource;

/** spec/conformance.json "contactCases": audiences and contacts. */
class ContactsConformanceTest {
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
        spec = MockServer.spec("conformance.json").get("contactCases");
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
        for (JsonNode c : MockServer.spec("conformance.json").get("contactCases").get("cases")) {
            cases.add(Arguments.of(Named.of(c.get("name").asText(), c)));
        }
        return cases.stream();
    }

    /** A string naming a fixture stands for it. */
    private static JsonNode fixture(JsonNode value) {
        return value.isTextual() && spec.has(value.asText()) ? spec.get(value.asText()) : value;
    }

    private static ContactInput contact(JsonNode value, JsonNode doubleOptIn) {
        JsonNode json = fixture(value);
        ContactInput.Builder b = ContactInput.builder(json.get("email").asText());
        if (json.has("first_name")) b.firstName(json.get("first_name").asText());
        if (json.has("last_name")) b.lastName(json.get("last_name").asText());
        if (json.has("fields")) {
            Map<String, String> fields = new LinkedHashMap<>();
            json.get("fields").fields().forEachRemaining(f -> fields.put(f.getKey(), f.getValue().asText()));
            b.fields(fields);
        }
        if (doubleOptIn != null) {
            JsonNode d = fixture(doubleOptIn);
            b.doubleOptIn(d.has("subject")
                    ? DoubleOptIn.from(d.get("from").asText(), d.get("subject").asText())
                    : DoubleOptIn.from(d.get("from").asText()));
        }
        return b.build();
    }

    private static Object run(JsonNode c) {
        Mailhive client = Mailhive.builder().apiKey(c.get("key").asText()).baseUrl(mock.url + "/v1").maxRetries(2).build();
        String audience = spec.get("audienceId").asText();
        JsonNode a = c.path("args");
        switch (c.get("call").asText()) {
            case "audiences.list":
                return client.audiences().list();
            case "audiences.create":
                return client.audiences().create(a.get("name").asText());
            case "audiences.rename":
                return client.audiences().rename(a.get("id").asText(), a.get("name").asText());
            case "contacts.add":
                return client.contacts().add(audience, contact(a.get("contact"), null));
            case "contacts.upsert":
                return client.contacts().upsert(audience, contact(a.get("contact"), a.get("doubleOptIn")));
            case "contacts.upsertBatch": {
                List<ContactInput> list = new ArrayList<>();
                for (JsonNode item : a.get("contacts")) list.add(contact(item, null));
                return client.contacts().upsertBatch(audience, list);
            }
            case "contacts.list": {
                ListContactsParams params = ListContactsParams.builder();
                if (a.has("email")) params.email(a.get("email").asText());
                if (a.has("status")) params.status(a.get("status").asText());
                if (a.has("page")) params.page(a.get("page").asInt());
                if (a.has("pageSize")) params.pageSize(a.get("pageSize").asInt());
                return client.contacts().list(audience, params);
            }
            case "contacts.get":
                return client.contacts().get(a.get("id").asText());
            case "contacts.update": {
                ContactUpdate.Builder b = ContactUpdate.builder();
                JsonNode changes = a.get("changes");
                if (changes.has("first_name")) b.firstName(changes.get("first_name").asText());
                if (changes.has("last_name")) b.lastName(changes.get("last_name").asText());
                if (changes.has("fields")) {
                    Map<String, String> fields = new LinkedHashMap<>();
                    changes.get("fields").fields().forEachRemaining(f -> fields.put(f.getKey(), f.getValue().asText()));
                    b.fields(fields);
                }
                return client.contacts().update(a.get("id").asText(), b.build());
            }
            case "contacts.unsubscribe":
                return client.contacts().unsubscribe(a.get("id").asText());
            case "contacts.delete":
                client.contacts().delete(a.get("id").asText());
                return null;
            default:
                throw new IllegalArgumentException(c.get("call").asText());
        }
    }

    private static JsonNode at(JsonNode node, String path) {
        for (String part : path.split("\\.")) {
            node = node.isArray() ? node.get(Integer.parseInt(part)) : node.get(part);
            if (node == null) {
                return Json.MAPPER.nullNode();
            }
        }
        return node;
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
        JsonNode actual = Json.MAPPER.valueToTree(result);
        if (want.has("resultFields")) {
            Iterator<Map.Entry<String, JsonNode>> fields = want.get("resultFields").fields();
            while (fields.hasNext()) {
                Map.Entry<String, JsonNode> f = fields.next();
                assertEquals(f.getValue(), at(actual, f.getKey()), "result " + f.getKey());
            }
        }
        if (want.has("resultCount")) {
            assertEquals(want.get("resultCount").asInt(), actual.size(), "result count");
        }
        if (want.path("resultIsEmpty").asBoolean(false)) {
            assertNull(result, "result");
            assertNull(error, "error");
        }
        if (want.has("minElapsedMs")) {
            assertTrue(elapsedMs >= want.get("minElapsedMs").asDouble(), "elapsed " + elapsedMs + "ms");
        }
        if (want.has("error")) {
            JsonNode expected = want.get("error");
            assertNotNull(error, "expected an error");
            Class<? extends ApiException> kind = KINDS.get(expected.get("kind").asText());
            assertTrue(kind.isInstance(error), "expected " + kind.getSimpleName() + ", got " + error);
            assertEquals(expected.get("status").asInt(), ((ApiException) error).status());
            assertEquals(expected.get("code").asText(), ((ApiException) error).code());
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
                    case "noIdempotencyKey": assertFalse(headers.has("idempotency-key"), message); break;
                    case "query": assertEquals(v, first.get("query"), message); break;
                    case "body": assertEquals(fixture(v), first.get("body"), message); break;
                    case "bodyIncludes": {
                        ObjectNode picked = Json.MAPPER.createObjectNode();
                        v.fieldNames().forEachRemaining(k -> picked.set(k, first.get("body").get(k)));
                        assertEquals(v, picked, message);
                        break;
                    }
                    case "bodyContactCount": assertEquals(v.asInt(), first.get("body").get("contacts").size(), message); break;
                    default: fail("Unknown request check " + check.getKey());
                }
            }
        }
    }
}
