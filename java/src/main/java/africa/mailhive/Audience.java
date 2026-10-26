package africa.mailhive;

import com.fasterxml.jackson.annotation.JsonAnyGetter;
import com.fasterxml.jackson.annotation.JsonAnySetter;
import com.fasterxml.jackson.annotation.JsonIgnoreProperties;
import com.fasterxml.jackson.annotation.JsonProperty;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.Map;

/** A list of contacts that broadcasts go to. */
@JsonIgnoreProperties(ignoreUnknown = true)
public final class Audience {
    @JsonProperty("id") private String id;
    @JsonProperty("name") private String name;
    @JsonProperty("created_at") private String createdAt;
    @JsonProperty("contacts") private int contacts;
    @JsonProperty("mailable") private int mailable;
    private final Map<String, Object> other = new LinkedHashMap<>();

    Audience() {
    }

    public String id() { return id; }
    public String name() { return name; }
    /** ISO 8601, or {@code null} (a rename doesn't return it). */
    public String createdAt() { return createdAt; }
    /** Everyone in the audience. */
    public int contacts() { return contacts; }
    /** Of those, how many a broadcast would reach. */
    public int mailable() { return mailable; }

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
        return "Audience(id=" + id + ", name=" + name + ")";
    }
}
