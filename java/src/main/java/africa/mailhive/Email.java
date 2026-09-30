package africa.mailhive;

import com.fasterxml.jackson.annotation.JsonAnyGetter;
import com.fasterxml.jackson.annotation.JsonAnySetter;
import com.fasterxml.jackson.annotation.JsonIgnoreProperties;
import com.fasterxml.jackson.annotation.JsonProperty;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/** A sent email and its latest status. */
@JsonIgnoreProperties(ignoreUnknown = true)
public final class Email {
    @JsonProperty("id") private String id;
    @JsonProperty("status") private String status;
    @JsonProperty("stream") private String stream;
    @JsonProperty("test") private boolean test;
    @JsonProperty("from") private String from;
    @JsonProperty("to") private List<String> to = Collections.emptyList();
    @JsonProperty("cc") private List<String> cc = Collections.emptyList();
    @JsonProperty("bcc") private List<String> bcc = Collections.emptyList();
    @JsonProperty("subject") private String subject;
    @JsonProperty("suppressed") private List<String> suppressed = Collections.emptyList();
    @JsonProperty("tags") private Map<String, String> tags = Collections.emptyMap();
    @JsonProperty("template_id") private String templateId;
    @JsonProperty("template_version") private Integer templateVersion;
    @JsonProperty("created_at") private String createdAt;
    @JsonProperty("sent_at") private String sentAt;
    @JsonProperty("last_event_at") private String lastEventAt;
    private final Map<String, Object> other = new LinkedHashMap<>();

    Email() {
    }

    public String id() { return id; }
    /** e.g. {@code queued}, {@code sent}, {@code delivered}, {@code bounced}. */
    public String status() { return status; }
    /** {@code transactional} or {@code broadcast}. */
    public String stream() { return stream; }
    public boolean test() { return test; }
    public String from() { return from; }
    public List<String> to() { return to; }
    public List<String> cc() { return cc; }
    public List<String> bcc() { return bcc; }
    public String subject() { return subject; }
    public List<String> suppressed() { return suppressed; }
    public Map<String, String> tags() { return tags; }
    public String templateId() { return templateId; }
    public Integer templateVersion() { return templateVersion; }
    /** ISO 8601, or {@code null}. */
    public String createdAt() { return createdAt; }
    public String sentAt() { return sentAt; }
    public String lastEventAt() { return lastEventAt; }

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
        return "Email(id=" + id + ", status=" + status + ", to=" + to + ", subject=" + subject + ")";
    }
}
