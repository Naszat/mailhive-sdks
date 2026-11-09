package africa.mailhive;

import com.fasterxml.jackson.annotation.JsonIgnoreProperties;
import com.fasterxml.jackson.annotation.JsonProperty;

/** What {@code emails().validate(...)} found out about one address. */
@JsonIgnoreProperties(ignoreUnknown = true)
public final class RecipientVerdict {
    @JsonProperty("email")
    private String email;
    @JsonProperty("normalized")
    private String normalized;
    @JsonProperty("valid")
    private boolean valid;
    @JsonProperty("reason")
    private String reason;
    @JsonProperty("message")
    private String message;
    @JsonProperty("suggestion")
    private String suggestion;

    RecipientVerdict() {
    }

    /** The address as given. */
    public String email() { return email; }

    /** The bare, lowercased address, or {@code null} when there isn't one. */
    public String normalized() { return normalized; }

    /** False when sending to this address would be refused. */
    public boolean valid() { return valid; }

    /**
     * Why it's invalid: {@code invalid_syntax}, {@code domain_typo}, {@code domain_not_found},
     * {@code no_mail_server} or {@code suppressed} (on your suppression list). {@code null} when valid.
     */
    public String reason() { return reason; }

    /** The reason in plain words, fit to show the person typing the address; {@code null} when valid. */
    public String message() { return message; }

    /** For {@code domain_typo}, the address that was probably meant (ada@gmail.com for ada@gmial.com); otherwise {@code null}. */
    public String suggestion() { return suggestion; }

    @Override
    public String toString() {
        return "RecipientVerdict(email=" + email + ", valid=" + valid + ", reason=" + reason + ", suggestion=" + suggestion + ")";
    }
}
