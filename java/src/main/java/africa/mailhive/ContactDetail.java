package africa.mailhive;

import com.fasterxml.jackson.annotation.JsonIgnoreProperties;
import com.fasterxml.jackson.annotation.JsonProperty;
import java.util.Collections;
import java.util.List;

/** A contact with the latest emails sent to them. */
@JsonIgnoreProperties(ignoreUnknown = true)
public final class ContactDetail extends Contact {
    @JsonProperty("history") private List<Email> history = Collections.emptyList();

    ContactDetail() {
    }

    /** The latest emails sent to them. */
    public List<Email> history() { return history; }
}
