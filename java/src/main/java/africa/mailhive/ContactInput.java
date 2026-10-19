package africa.mailhive;

import com.fasterxml.jackson.databind.node.ObjectNode;
import java.util.LinkedHashMap;
import java.util.Map;
import java.util.Objects;

/**
 * A person to add or update. JSON names match the API reference; anything you don't set is left
 * out, so an update keeps it.
 *
 * <pre>{@code
 * ContactInput.builder("ada@example.com").firstName("Ada").field("plan", "pro").build();
 * }</pre>
 */
public final class ContactInput {
    private final String email;
    private final boolean hasFirstName;
    private final String firstName;
    private final boolean hasLastName;
    private final String lastName;
    private final Map<String, String> fields;
    private final String status;
    private final DoubleOptIn doubleOptIn;

    private ContactInput(Builder b) {
        this.email = b.email;
        this.hasFirstName = b.hasFirstName;
        this.firstName = b.firstName;
        this.hasLastName = b.hasLastName;
        this.lastName = b.lastName;
        this.fields = new LinkedHashMap<>(b.fields);
        this.status = b.status;
        this.doubleOptIn = b.doubleOptIn;
    }

    public static Builder builder(String email) {
        return new Builder(email);
    }

    ObjectNode toJson() {
        ObjectNode node = Json.MAPPER.createObjectNode();
        node.put("email", email);
        if (hasFirstName) {
            node.put("first_name", firstName);
        }
        if (hasLastName) {
            node.put("last_name", lastName);
        }
        if (!fields.isEmpty()) {
            ObjectNode map = node.putObject("fields");
            fields.forEach(map::put);
        }
        if (status != null) {
            node.put("status", status);
        }
        if (doubleOptIn != null) {
            ObjectNode opt = node.putObject("double_opt_in");
            opt.put("from", doubleOptIn.fromAddress());
            if (doubleOptIn.subject() != null) {
                opt.put("subject", doubleOptIn.subject());
            }
        }
        return node;
    }

    public static final class Builder {
        private final String email;
        private boolean hasFirstName;
        private String firstName;
        private boolean hasLastName;
        private String lastName;
        private final Map<String, String> fields = new LinkedHashMap<>();
        private String status;
        private DoubleOptIn doubleOptIn;

        private Builder(String email) {
            this.email = Objects.requireNonNull(email, "email");
        }

        /** Sets the name; {@code ""} (or {@code null}) clears it. Leave it out to keep it. */
        public Builder firstName(String firstName) { this.hasFirstName = true; this.firstName = firstName; return this; }
        public Builder lastName(String lastName) { this.hasLastName = true; this.lastName = lastName; return this; }

        /** Custom fields, merged into the contact's; an empty value removes one. */
        public Builder fields(Map<String, String> fields) {
            this.fields.clear();
            if (fields != null) {
                this.fields.putAll(fields);
            }
            return this;
        }

        public Builder field(String name, String value) { fields.put(name, value); return this; }

        /**
         * {@code "subscribed"} or {@code "unsubscribed"}. Someone who unsubscribed or reported
         * spam can't be set to subscribed: use {@link #doubleOptIn}.
         */
        public Builder status(String status) { this.status = status; return this; }

        public Builder doubleOptIn(DoubleOptIn doubleOptIn) { this.doubleOptIn = doubleOptIn; return this; }

        public ContactInput build() {
            return new ContactInput(this);
        }
    }
}
