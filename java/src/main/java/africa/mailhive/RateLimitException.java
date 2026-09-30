package africa.mailhive;

import com.fasterxml.jackson.databind.JsonNode;
import java.util.List;
import java.util.Map;
import java.util.Optional;

/**
 * 429: {@code rate_limited} (retried for you), or {@code monthly_quota_reached}
 * / {@code daily_cap_reached} (not retried: waiting won't help).
 */
public class RateLimitException extends ApiException {
    private static final long serialVersionUID = 1L;

    public RateLimitException(String message, int status, String code, JsonNode details, String requestId,
            Map<String, List<String>> headers) {
        super(message, status, code, details, requestId, headers);
    }

    /** The {@code Retry-After} header, in seconds, when the API sent one. */
    public Optional<Double> retryAfter() {
        return header("Retry-After").flatMap(Transport::parseSeconds);
    }
}
