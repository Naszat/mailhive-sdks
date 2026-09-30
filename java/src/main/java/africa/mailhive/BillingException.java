package africa.mailhive;

import com.fasterxml.jackson.databind.JsonNode;
import java.util.List;
import java.util.Map;

/** 402: Mailhive Send is paused over an unpaid invoice. */
public class BillingException extends ApiException {
    private static final long serialVersionUID = 1L;

    public BillingException(String message, int status, String code, JsonNode details, String requestId,
            Map<String, List<String>> headers) {
        super(message, status, code, details, requestId, headers);
    }
}
