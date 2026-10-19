package africa.mailhive;

import com.fasterxml.jackson.annotation.JsonAnyGetter;
import com.fasterxml.jackson.annotation.JsonAnySetter;
import com.fasterxml.jackson.annotation.JsonIgnoreProperties;
import com.fasterxml.jackson.annotation.JsonProperty;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.Map;

/** A person in an audience. */
@JsonIgnoreProperties(ignoreUnknown = true)
public class Contact {
    @JsonProperty("id") private String id;
    @JsonProperty("audience_id") private String audienceId;
    @JsonProperty("email") private String email;
    @JsonProperty("first_name") private String firstName;
    @JsonProperty("last_name") private String lastName;
    @JsonProperty("fields") private Map<String, String> fields = Collections.emptyMap();
    @JsonProperty("status") private String status;
    @JsonProperty("suppressed") private boolean suppressed;
    @JsonProperty("mailable") private boolean mailable;
    @JsonProperty("source") private String source;
    @JsonProperty("created_at") private String createdAt;
    @JsonProperty("unsubscribed_at") private String unsubscribedAt;
    @JsonProperty("test") private boolean test;
    private final Map<String, Object> other = new LinkedHashMap<>();

    Contact() {
    }

    public String id() { return id; }
    public String audienceId() { return audienceId; }
    public String email() { return email; }
    /** {@code null} when not set. */
    public String firstName() { return firstName; }
    public String lastName() { return lastName; }
    public Map<String, String> fields() { return fields; }
    /** {@code subscribed}, {@code unsubscribed} or {@code pending}. */
    public String status() { return status; }
    /** On the suppression list (bounce, spam complaint or unsubscribe): never mailed. */
    public boolean suppressed() { return suppressed; }
    /** Subscribed and not suppressed: a broadcast would reach them. */
    public boolean mailable() { return mailable; }
    /** {@code manual}, {@code import}, {@code api} or {@code form}. */
    public String source() { return source; }
    public String createdAt() { return createdAt; }
    public String unsubscribedAt() { return unsubscribedAt; }
    /** True when a test key made the call: nothing was saved or sent. */
    public boolean test() { return test; }

    /** Fields this SDK version doesn't know about yet. */
    @JsonAnyGetter
    public Map<String, Object> other() {
        return Collections.unmodifiableMap(other);
    }

    @JsonAnySetter
    void setOther(String name, Object value) {
        other.put(name, value);
    }

    @Override
    public String toString() {
        return "Contact(id=" + id + ", email=" + email + ", status=" + status + ")";
    }
}
