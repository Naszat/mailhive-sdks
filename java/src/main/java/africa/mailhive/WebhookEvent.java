package africa.mailhive;

import com.fasterxml.jackson.core.type.TypeReference;
import com.fasterxml.jackson.databind.JsonNode;
import java.util.Map;

/** A verified webhook event, e.g. {@code email.delivered}. */
public final class WebhookEvent {
    private final JsonNode json;

    WebhookEvent(JsonNode json) {
        this.json = json;
    }

    /** e.g. {@code email.delivered}, {@code email.bounced}. */
    public String type() {
        return text("type");
    }

    /** ISO 8601. */
    public String createdAt() {
        return text("created_at");
    }

    /** True for a test event sent from the dashboard. */
    public boolean test() {
        return json.path("test").asBoolean(false);
    }

    /** The event's data: {@code email_id}, {@code recipient}, … */
    public JsonNode data() {
        return json.path("data");
    }

    /** The event's data as a map. */
    public Map<String, Object> dataAsMap() {
        return Json.MAPPER.convertValue(data(), new TypeReference<Map<String, Object>>() {});
    }

    /** The whole event, as parsed. */
    public JsonNode json() {
        return json;
    }

    private String text(String field) {
        JsonNode node = json.get(field);
        return node == null || node.isNull() ? null : node.asText();
    }

    @Override
    public String toString() {
        return "WebhookEvent(type=" + type() + ", created_at=" + createdAt() + ")";
    }
}
