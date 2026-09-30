package africa.mailhive;

import com.fasterxml.jackson.core.type.TypeReference;
import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.node.ArrayNode;
import com.fasterxml.jackson.databind.node.ObjectNode;
import java.net.URLEncoder;
import java.nio.charset.StandardCharsets;
import java.util.List;
import java.util.Objects;

/** Sending emails and looking them up: {@code client.emails()}. */
public final class Emails {
    private final Transport transport;

    Emails(Transport transport) {
        this.transport = transport;
    }

    /** Sends one email. */
    public AcceptedEmail send(SendEmailParams params) {
        return send(params, RequestOptions.none());
    }

    /** Sends one email, e.g. with {@code RequestOptions.idempotencyKey("order-1042-receipt")}. */
    public AcceptedEmail send(SendEmailParams params, RequestOptions options) {
        Objects.requireNonNull(params, "params");
        JsonNode response = transport.request("POST", "/send/emails", params.toJson(Json.MAPPER), key(options));
        return Json.MAPPER.convertValue(response, AcceptedEmail.class);
    }

    /** Sends up to 100 independent emails; all are accepted, or none. */
    public List<AcceptedEmail> sendBatch(List<SendEmailParams> emails) {
        return sendBatch(emails, RequestOptions.none());
    }

    /** Sends up to 100 independent emails, with options. */
    public List<AcceptedEmail> sendBatch(List<SendEmailParams> emails, RequestOptions options) {
        Objects.requireNonNull(emails, "emails");
        ObjectNode body = Json.MAPPER.createObjectNode();
        ArrayNode list = body.putArray("emails");
        for (SendEmailParams email : emails) {
            list.add(email.toJson(Json.MAPPER));
        }
        JsonNode response = transport.request("POST", "/send/emails/batch", body, key(options));
        return Json.MAPPER.convertValue(response.path("data"), new TypeReference<List<AcceptedEmail>>() {});
    }

    /** Gets an email and its latest status. */
    public Email get(String id) {
        Objects.requireNonNull(id, "id");
        JsonNode response = transport.request("GET", "/send/emails/" + encode(id), null, null);
        return Json.MAPPER.convertValue(response, Email.class);
    }

    private static String key(RequestOptions options) {
        return options == null ? null : options.getIdempotencyKey();
    }

    private static String encode(String segment) {
        return URLEncoder.encode(segment, StandardCharsets.UTF_8)
                .replace("+", "%20").replace("*", "%2A").replace("%7E", "~");
    }
}
