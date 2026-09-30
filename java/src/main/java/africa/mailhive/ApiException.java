package africa.mailhive;

import com.fasterxml.jackson.databind.JsonNode;
import java.util.Collections;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.TreeMap;

/**
 * An error response from the API. Check {@link #code()}, Mailhive's stable
 * error code, rather than the message, and quote {@link #requestId()} to
 * support.
 */
public class ApiException extends MailhiveException {
    private static final long serialVersionUID = 1L;

    private final int status;
    private final String code;
    private final transient JsonNode details;
    private final String requestId;
    private final Map<String, List<String>> headers;

    public ApiException(
            String message,
            int status,
            String code,
            JsonNode details,
            String requestId,
            Map<String, List<String>> headers) {
        super(message);
        this.status = status;
        this.code = code;
        this.details = details == null || details.isNull() || details.isMissingNode() ? null : details;
        this.requestId = requestId;
        TreeMap<String, List<String>> copy = new TreeMap<>(String.CASE_INSENSITIVE_ORDER);
        if (headers != null) {
            copy.putAll(headers);
        }
        this.headers = Collections.unmodifiableMap(copy);
    }

    /** The HTTP status. */
    public int status() {
        return status;
    }

    /** Mailhive's error code, e.g. {@code validation_error}; {@code http_error} when the body wasn't JSON. */
    public String code() {
        return code;
    }

    /** Extra information, such as every validation problem; {@code null} when there is none. */
    public JsonNode details() {
        return details;
    }

    /** The request id to quote to support, or {@code null}. */
    public String requestId() {
        return requestId;
    }

    /** The response headers (names are case-insensitive). */
    public Map<String, List<String>> headers() {
        return headers;
    }

    /** The first value of a response header. */
    public Optional<String> header(String name) {
        List<String> values = headers.get(name);
        return values == null || values.isEmpty() ? Optional.empty() : Optional.ofNullable(values.get(0));
    }

    @Override
    public String toString() {
        return getClass().getSimpleName() + "(status=" + status + ", code=" + code + ", request_id=" + requestId + "): "
                + getMessage();
    }
}
