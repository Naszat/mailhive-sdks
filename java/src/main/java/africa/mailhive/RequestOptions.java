package africa.mailhive;

import java.util.Objects;

/** Per-request options, such as your own idempotency key. */
public final class RequestOptions {
    private static final RequestOptions NONE = new RequestOptions(null);

    private final String idempotencyKey;

    private RequestOptions(String idempotencyKey) {
        this.idempotencyKey = idempotencyKey;
    }

    /** No options: the SDK generates an idempotency key for each call. */
    public static RequestOptions none() {
        return NONE;
    }

    /**
     * Sends with your own {@code Idempotency-Key}, e.g. {@code "order-1042-receipt"}, so a
     * send stays safe to repeat across restarts.
     */
    public static RequestOptions idempotencyKey(String key) {
        return new RequestOptions(Objects.requireNonNull(key, "key"));
    }

    /** The caller's idempotency key, or {@code null}. */
    public String getIdempotencyKey() {
        return idempotencyKey;
    }
}
