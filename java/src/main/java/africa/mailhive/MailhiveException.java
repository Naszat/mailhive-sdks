package africa.mailhive;

/** Base class for everything this SDK throws. Unchecked. */
public class MailhiveException extends RuntimeException {
    private static final long serialVersionUID = 1L;

    public MailhiveException(String message) {
        super(message);
    }

    public MailhiveException(String message, Throwable cause) {
        super(message, cause);
    }
}
