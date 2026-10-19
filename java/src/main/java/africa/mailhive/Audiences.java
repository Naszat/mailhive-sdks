package africa.mailhive;

import com.fasterxml.jackson.core.type.TypeReference;
import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.node.ObjectNode;
import java.util.List;
import java.util.Objects;

/** Audiences, the lists your broadcasts go to: {@code client.audiences()}. Needs an API key with Full access. */
public final class Audiences {
    private final Transport transport;

    Audiences(Transport transport) {
        this.transport = transport;
    }

    public List<Audience> list() {
        return Json.MAPPER.convertValue(transport.request("GET", "/send/audiences", null, null), new TypeReference<List<Audience>>() {});
    }

    public Audience create(String name) {
        return create(name, RequestOptions.none());
    }

    public Audience create(String name, RequestOptions options) {
        Objects.requireNonNull(name, "name");
        ObjectNode body = Json.MAPPER.createObjectNode().put("name", name);
        JsonNode response = transport.request("POST", "/send/audiences", body, options == null ? null : options.getIdempotencyKey());
        return Json.MAPPER.convertValue(response, Audience.class);
    }

    /** Changes an audience's name. */
    public Audience rename(String id, String name) {
        Objects.requireNonNull(id, "id");
        Objects.requireNonNull(name, "name");
        ObjectNode body = Json.MAPPER.createObjectNode().put("name", name);
        return Json.MAPPER.convertValue(transport.request("PATCH", "/send/audiences/" + Contacts.encode(id), body, null), Audience.class);
    }
}
