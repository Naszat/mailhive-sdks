package africa.mailhive;

import com.fasterxml.jackson.annotation.JsonIgnoreProperties;
import com.fasterxml.jackson.annotation.JsonProperty;
import java.util.Collections;
import java.util.List;

/** An email the API accepted for sending. */
@JsonIgnoreProperties(ignoreUnknown = true)
public final class AcceptedEmail {
    @JsonProperty("id")
    private String id;
    @JsonProperty("status")
    private String status;
    @JsonProperty("suppressed")
    private List<String> suppressed = Collections.emptyList();
    @JsonProperty("test")
    private boolean test;

    AcceptedEmail() {
    }

    /** The email's id, for {@code emails().get(id)} and webhooks. */
    public String id() { return id; }

    /** {@code queued}, or another status for special cases. */
    public String status() { return status; }

    /** Recipients left out because they're on the suppression list. */
    public List<String> suppressed() { return suppressed; }

    /** True when sent with a test key ({@code mhs_test_…}): delivery is simulated. */
    public boolean test() { return test; }

    @Override
    public String toString() {
        return "AcceptedEmail(id=" + id + ", status=" + status + ", suppressed=" + suppressed + ", test=" + test + ")";
    }
}
