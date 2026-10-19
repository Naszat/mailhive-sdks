package africa.mailhive;

import com.fasterxml.jackson.databind.node.ObjectNode;
import java.util.LinkedHashMap;
import java.util.Map;

/** Changes to an existing contact. Anything you don't set is left as it is. */
public final class ContactUpdate {
    private final boolean hasFirstName;
    private final String firstName;
    private final boolean hasLastName;
    private final String lastName;
    private final Map<String, String> fields;
    private final boolean unsubscribe;

    private ContactUpdate(Builder b) {
        this.hasFirstName = b.hasFirstName;
        this.firstName = b.firstName;
        this.hasLastName = b.hasLastName;
        this.lastName = b.lastName;
        this.fields = b.fields == null ? null : new LinkedHashMap<>(b.fields);
        this.unsubscribe = b.unsubscribe;
    }

    public static Builder builder() {
        return new Builder();
    }

    ObjectNode toJson() {
        ObjectNode node = Json.MAPPER.createObjectNode();
        if (hasFirstName) {
            node.put("first_name", firstName);
        }
        if (hasLastName) {
            node.put("last_name", lastName);
        }
        if (fields != null) {
            ObjectNode map = node.putObject("fields");
            fields.forEach(map::put);
        }
        if (unsubscribe) {
            node.put("unsubscribe", true);
        }
        return node;
    }

    public static final class Builder {
        private boolean hasFirstName;
        private String firstName;
        private boolean hasLastName;
        private String lastName;
        private Map<String, String> fields;
        private boolean unsubscribe;

        private Builder() {
        }

        /** Sets the name; {@code ""} (or {@code null}) clears it. */
        public Builder firstName(String firstName) { this.hasFirstName = true; this.firstName = firstName; return this; }
        public Builder lastName(String lastName) { this.hasLastName = true; this.lastName = lastName; return this; }

        /** Replaces the contact's custom fields (an empty map removes them all). Use upsert to merge. */
        public Builder fields(Map<String, String> fields) {
            this.fields = fields == null ? new LinkedHashMap<>() : new LinkedHashMap<>(fields);
            return this;
        }

        /** Unsubscribes the contact. Only they can undo it, by confirming a double opt-in email. */
        public Builder unsubscribe() { this.unsubscribe = true; return this; }

        public ContactUpdate build() {
            return new ContactUpdate(this);
        }
    }
}
