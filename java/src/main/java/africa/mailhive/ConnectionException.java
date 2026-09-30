package africa.mailhive;

/** The API couldn't be reached, or didn't answer in time. */
public class ConnectionException extends MailhiveException {
    private static final long serialVersionUID = 1L;

    public ConnectionException(String message, Throwable cause) {
        super(message, cause);
    }
}
