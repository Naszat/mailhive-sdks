package africa.mailhive;

import com.fasterxml.jackson.annotation.JsonIgnoreProperties;
import com.fasterxml.jackson.annotation.JsonProperty;
import java.util.Collections;
import java.util.List;

/** The outcome of {@code contacts().upsertBatch}: one entry per contact sent, in order. */
@JsonIgnoreProperties(ignoreUnknown = true)
public final class ContactBatchResult {
    @JsonProperty("created") private int created;
    @JsonProperty("updated") private int updated;
    @JsonProperty("failed") private int failed;
    @JsonProperty("results") private List<Entry> results = Collections.emptyList();

    ContactBatchResult() {
    }

    public int created() { return created; }
    public int updated() { return updated; }
    public int failed() { return failed; }
    /** A failed entry here doesn't throw: check {@link Entry#ok()}. */
    public List<Entry> results() { return results; }

    /** The result for one contact. */
    @JsonIgnoreProperties(ignoreUnknown = true)
    public static final class Entry {
        @JsonProperty("index") private int index;
        @JsonProperty("email") private String email;
        @JsonProperty("ok") private boolean ok;
        @JsonProperty("created") private boolean created;
        @JsonProperty("contact") private Contact contact;
        @JsonProperty("error") private Error error;

        Entry() {
        }

        public int index() { return index; }
        public String email() { return email; }
        public boolean ok() { return ok; }
        /** Whether this call made the contact (rather than updating one). */
        public boolean created() { return created; }
        /** The saved contact, when {@link #ok()}. */
        public Contact contact() { return contact; }
        /** Why it failed, or {@code null}. */
        public Error error() { return error; }
    }

    /** Why one entry failed. */
    @JsonIgnoreProperties(ignoreUnknown = true)
    public static final class Error {
        @JsonProperty("code") private String code;
        @JsonProperty("message") private String message;

        Error() {
        }

        public String code() { return code; }
        public String message() { return message; }
    }
}
