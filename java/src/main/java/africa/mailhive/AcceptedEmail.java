package africa.mailhive;

import com.fasterxml.jackson.annotation.JsonIgnoreProperties;
import com.fasterxml.jackson.annotation.JsonProperty;
import java.util.Collections;
import java.util.List;
import java.util.Map;

/** An email the API accepted for sending. */
@JsonIgnoreProperties(ignoreUnknown = true)
public final class AcceptedEmail {
    @JsonProperty("id")
    private String id;
    @JsonProperty("status")
    private String status;
    @JsonProperty("suppressed")
    private List<String> suppressed = Collections.emptyList();
    @JsonProperty("rejected")
    private Map<String, String> rejected = Collections.emptyMap();
    @JsonProperty("test")
    private boolean test;

    AcceptedEmail() {
    }

    /** The email's id, for {@code emails().get(id)} and webhooks. */
    public String id() { return id; }

    /** {@code queued}, or {@code suppressed} when no recipient was left after the suppression list and the recipient checks. */
    public String status() { return status; }

    /** Recipients left out because they're on the suppression list. */
    public List<String> suppressed() { return suppressed; }

    /**
     * Recipients dropped before sending because the address can't work, keyed by address, with the reason:
     * {@code invalid_syntax}, {@code domain_typo} (gmial.com), {@code domain_not_found} or {@code no_mail_server}.
     * They never reach the sending engine, so they can't bounce. Empty when every address passed.
     */
    public Map<String, String> rejected() { return rejected; }

    /** True when sent with a test key ({@code mhs_test_…}): delivery is simulated. */
    public boolean test() { return test; }

    @Override
    public String toString() {
        return "AcceptedEmail(id=" + id + ", status=" + status + ", suppressed=" + suppressed + ", rejected=" + rejected + ", test=" + test + ")";
    }
}
