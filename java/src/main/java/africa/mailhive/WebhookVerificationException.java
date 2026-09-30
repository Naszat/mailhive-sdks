package africa.mailhive;

/**
 * A webhook's signature didn't check out. {@link #reason()} is
 * {@code header}, {@code timestamp} or {@code signature}.
 */
public class WebhookVerificationException extends MailhiveException {
    private static final long serialVersionUID = 1L;

    private final String reason;

    public WebhookVerificationException(String message, String reason) {
        super(message);
        this.reason = reason;
    }

    /** Why verification failed: {@code header}, {@code timestamp} or {@code signature}. */
    public String reason() {
        return reason;
    }
}
