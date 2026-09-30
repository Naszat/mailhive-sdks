package africa.mailhive;

import com.fasterxml.jackson.databind.JsonNode;
import java.util.List;
import java.util.Map;

/** 403: Send isn't activated, or the stream is paused. */
public class PermissionException extends ApiException {
    private static final long serialVersionUID = 1L;

    public PermissionException(String message, int status, String code, JsonNode details, String requestId,
            Map<String, List<String>> headers) {
        super(message, status, code, details, requestId, headers);
    }
}
