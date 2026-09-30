package africa.mailhive;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.fasterxml.jackson.databind.JsonNode;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.time.Instant;
import java.util.ArrayList;
import java.util.List;
import java.util.stream.Stream;
import org.junit.jupiter.api.Named;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.Arguments;
import org.junit.jupiter.params.provider.MethodSource;

/** spec/webhook-vectors.json: signatures made by the backend's own signing function. */
class WebhooksTest {
    static Stream<Arguments> vectors() throws IOException {
        List<Arguments> cases = new ArrayList<>();
        for (JsonNode c : MockServer.spec("webhook-vectors.json").get("cases")) {
            cases.add(Arguments.of(Named.of(c.get("name").asText(), c)));
        }
        return cases.stream();
    }

    @ParameterizedTest(name = "{0}")
    @MethodSource("vectors")
    void vector(JsonNode c) throws IOException {
        long tolerance = MockServer.spec("webhook-vectors.json").get("tolerance_seconds").asLong();
        String payload = c.get("payload").asText();
        String header = c.get("header").asText();
        String secret = c.get("secret").asText();
        Instant now = Instant.ofEpochSecond(c.get("now").asLong());
        if (c.get("valid").asBoolean()) {
            WebhookEvent event = Webhooks.verify(payload, header, secret, Duration.ofSeconds(tolerance), now);
            JsonNode expected = Json.MAPPER.readTree(payload);
            assertEquals(expected, event.json());
            assertEquals(expected.get("type").asText(), event.type());
            assertEquals(expected.get("created_at").asText(), event.createdAt());
            assertEquals(expected.get("data"), event.data());
            assertEquals(expected.get("data").get("email_id").asText(), event.dataAsMap().get("email_id"));
            // The byte[] overload agrees.
            assertEquals(expected, Webhooks.verify(payload.getBytes(StandardCharsets.UTF_8), header, secret,
                    Duration.ofSeconds(tolerance), now).json());
        } else {
            WebhookVerificationException error = assertThrows(WebhookVerificationException.class,
                    () -> Webhooks.verify(payload, header, secret, Duration.ofSeconds(tolerance), now));
            assertEquals(c.get("reason").asText(), error.reason());
        }
    }

    @Test
    void missingHeaderIsAHeaderError() {
        WebhookVerificationException error = assertThrows(WebhookVerificationException.class,
                () -> Webhooks.verify("{}", null, "whsec_x"));
        assertEquals("header", error.reason());
    }

    @Test
    void defaultClockRejectsAnOldSignature() throws IOException {
        JsonNode c = MockServer.spec("webhook-vectors.json").get("cases").get(0);
        WebhookVerificationException error = assertThrows(WebhookVerificationException.class,
                () -> Webhooks.verify(c.get("payload").asText(), c.get("header").asText(), c.get("secret").asText()));
        assertEquals("timestamp", error.reason());
        assertTrue(error.getMessage().contains("300 seconds"));
    }
}
