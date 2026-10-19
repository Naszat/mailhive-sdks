package africa.mailhive;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.node.ArrayNode;
import com.fasterxml.jackson.databind.node.ObjectNode;
import java.net.URLEncoder;
import java.nio.charset.StandardCharsets;
import java.util.List;
import java.util.Objects;

/**
 * The people in an audience: {@code client.contacts()}. Needs an API key with Full access.
 *
 * <p>Someone who unsubscribed, or reported your email as spam, can't be made subscribed again by
 * these calls: you get a {@link ConflictException} with code {@code contact_unsubscribed}. Use
 * {@link DoubleOptIn} to email them a confirmation link instead.
 */
public final class Contacts {
    private final Transport transport;

    Contacts(Transport transport) {
        this.transport = transport;
    }

    /** A page of contacts. */
    public ContactPage list(String audienceId) {
        return list(audienceId, ListContactsParams.builder());
    }

    /** A page of contacts; {@code params.email(…)} finds one person. */
    public ContactPage list(String audienceId, ListContactsParams params) {
        Objects.requireNonNull(audienceId, "audienceId");
        String query = params == null ? "" : params.queryString();
        JsonNode response = transport.request("GET", audiencePath(audienceId) + query, null, null);
        return Json.MAPPER.convertValue(response, ContactPage.class);
    }

    /** Adds one person. A {@link ConflictException} {@code contact_exists} if they're already there: use upsert. */
    public Contact add(String audienceId, ContactInput contact) {
        return add(audienceId, contact, RequestOptions.none());
    }

    public Contact add(String audienceId, ContactInput contact, RequestOptions options) {
        Objects.requireNonNull(audienceId, "audienceId");
        Objects.requireNonNull(contact, "contact");
        JsonNode response = transport.request("POST", audiencePath(audienceId), contact.toJson(), options == null ? null : options.getIdempotencyKey());
        return Json.MAPPER.convertValue(response, Contact.class);
    }

    /** Adds the person, or updates them if they're already there. Safe to repeat. */
    public Contact upsert(String audienceId, ContactInput contact) {
        Objects.requireNonNull(audienceId, "audienceId");
        Objects.requireNonNull(contact, "contact");
        return Json.MAPPER.convertValue(transport.request("PUT", audiencePath(audienceId), contact.toJson(), null), Contact.class);
    }

    /** Adds or updates up to 100 people. A bad entry doesn't throw: check each result's {@code ok()}. */
    public ContactBatchResult upsertBatch(String audienceId, List<ContactInput> contacts) {
        return upsertBatch(audienceId, contacts, RequestOptions.none());
    }

    public ContactBatchResult upsertBatch(String audienceId, List<ContactInput> contacts, RequestOptions options) {
        Objects.requireNonNull(audienceId, "audienceId");
        Objects.requireNonNull(contacts, "contacts");
        ObjectNode body = Json.MAPPER.createObjectNode();
        ArrayNode list = body.putArray("contacts");
        for (ContactInput contact : contacts) {
            list.add(contact.toJson());
        }
        JsonNode response = transport.request("POST", audiencePath(audienceId) + "/batch", body, options == null ? null : options.getIdempotencyKey());
        return Json.MAPPER.convertValue(response, ContactBatchResult.class);
    }

    /** A contact and the latest emails sent to them. */
    public ContactDetail get(String id) {
        Objects.requireNonNull(id, "id");
        return Json.MAPPER.convertValue(transport.request("GET", contactPath(id), null, null), ContactDetail.class);
    }

    /** Edits names and fields (fields are replaced, not merged: use upsert to merge), or unsubscribes. */
    public Contact update(String id, ContactUpdate changes) {
        Objects.requireNonNull(id, "id");
        Objects.requireNonNull(changes, "changes");
        return Json.MAPPER.convertValue(transport.request("PATCH", contactPath(id), changes.toJson(), null), Contact.class);
    }

    /** Unsubscribes them. Only they can undo it, by confirming a double opt-in email. */
    public Contact unsubscribe(String id) {
        return update(id, ContactUpdate.builder().unsubscribe().build());
    }

    /** Deleting someone who unsubscribed keeps their address suppressed. */
    public void delete(String id) {
        Objects.requireNonNull(id, "id");
        transport.request("DELETE", contactPath(id), null, null);
    }

    private static String audiencePath(String audienceId) {
        return "/send/audiences/" + encode(audienceId) + "/contacts";
    }

    private static String contactPath(String id) {
        return "/send/contacts/" + encode(id);
    }

    static String encode(String segment) {
        return URLEncoder.encode(segment, StandardCharsets.UTF_8)
                .replace("+", "%20").replace("*", "%2A").replace("%7E", "~");
    }
}
