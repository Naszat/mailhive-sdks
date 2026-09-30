package africa.mailhive;

import com.fasterxml.jackson.databind.JsonNode;
import java.util.List;
import java.util.Map;

/** 404: the email (or route) doesn't exist. */
public class NotFoundException extends ApiException {
    private static final long serialVersionUID = 1L;

    public NotFoundException(String message, int status, String code, JsonNode details, String requestId,
            Map<String, List<String>> headers) {
        super(message, status, code, details, requestId, headers);
    }
}
