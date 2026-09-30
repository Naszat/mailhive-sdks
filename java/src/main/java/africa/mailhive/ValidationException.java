package africa.mailhive;

import com.fasterxml.jackson.databind.JsonNode;
import java.util.List;
import java.util.Map;

/** 400 or 422: the request is invalid; {@link #details()} lists every problem. */
public class ValidationException extends ApiException {
    private static final long serialVersionUID = 1L;

    public ValidationException(String message, int status, String code, JsonNode details, String requestId,
            Map<String, List<String>> headers) {
        super(message, status, code, details, requestId, headers);
    }
}
