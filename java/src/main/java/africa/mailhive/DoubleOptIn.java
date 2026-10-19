package africa.mailhive;

import java.util.Objects;

/**
 * Emails the person a confirmation link first: they're subscribed only if they click it. Until
 * then they're {@code pending} and never mailed.
 */
public final class DoubleOptIn {
    private final String from;
    private final String subject;

    private DoubleOptIn(String from, String subject) {
        this.from = Objects.requireNonNull(from, "from");
        this.subject = subject;
    }

    /** From a verified sending domain, e.g. {@code "Acme <news@example.com>"}. */
    public static DoubleOptIn from(String from) {
        return new DoubleOptIn(from, null);
    }

    /** Also sets the subject (default: "Please confirm your subscription"). */
    public static DoubleOptIn from(String from, String subject) {
        return new DoubleOptIn(from, Objects.requireNonNull(subject, "subject"));
    }

    String fromAddress() { return from; }
    String subject() { return subject; }
}
