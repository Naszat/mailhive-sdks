package africa.mailhive;

import com.fasterxml.jackson.databind.JsonNode;
import java.util.List;
import java.util.Map;

/** 409: e.g. an Idempotency-Key reused for a different request. */
public class ConflictException extends ApiException {
    private static final long serialVersionUID = 1L;

    public ConflictException(String message, int status, String code, JsonNode details, String requestId,
            Map<String, List<String>> headers) {
        super(message, status, code, details, requestId, headers);
    }
}
