package africa.mailhive;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.node.NullNode;
import java.io.IOException;
import java.math.BigDecimal;
import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.net.http.HttpTimeoutException;
import java.time.Duration;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.UUID;
import java.util.concurrent.ThreadLocalRandom;

/** Sends requests with the shared retry, idempotency and error rules. */
final class Transport {
    static final double MAX_RETRY_AFTER = 60.0;

    /** Waits between retries. Swapped out in tests. */
    interface Sleeper {
        void sleep(double seconds) throws InterruptedException;
    }

    static final Sleeper THREAD_SLEEP = seconds -> Thread.sleep((long) Math.ceil(seconds * 1000));

    private final String apiKey;
    private final String baseUrl;
    private final Duration timeout;
    private final int maxRetries;
    private final HttpClient http;
    private final Sleeper sleeper;
    private final String userAgent;

    Transport(String apiKey, String baseUrl, Duration timeout, int maxRetries, HttpClient http, Sleeper sleeper) {
        this.apiKey = apiKey;
        this.baseUrl = baseUrl;
        this.timeout = timeout;
        this.maxRetries = maxRetries;
        this.http = http;
        this.sleeper = sleeper;
        this.userAgent = "mailhive-java/" + Version.SDK + " java/" + System.getProperty("java.version");
    }

    String baseUrl() {
        return baseUrl;
    }

    Duration timeout() {
        return timeout;
    }

    int maxRetries() {
        return maxRetries;
    }

    JsonNode request(String method, String path, JsonNode body, String idempotencyKey) {
        HttpRequest.Builder builder = HttpRequest.newBuilder(URI.create(baseUrl + path))
                .timeout(timeout)
                .header("Authorization", "Bearer " + apiKey)
                .header("Accept", "application/json")
                .header("User-Agent", userAgent);
        if (body != null) {
            byte[] bytes;
            try {
                bytes = Json.MAPPER.writeValueAsBytes(body);
            } catch (IOException e) {
                throw new MailhiveException("Couldn't encode the request as JSON.", e);
            }
            builder.header("Content-Type", "application/json");
            builder.method(method, HttpRequest.BodyPublishers.ofByteArray(bytes));
        } else {
            builder.method(method, HttpRequest.BodyPublishers.noBody());
        }
        if ("POST".equals(method)) {
            // One key per call, reused on every retry: a retry never sends twice.
            builder.header("Idempotency-Key", idempotencyKey != null ? idempotencyKey : UUID.randomUUID().toString());
        }
        HttpRequest request = builder.build();

        for (int attempt = 0; ; attempt++) {
            HttpResponse<byte[]> response;
            try {
                response = http.send(request, HttpResponse.BodyHandlers.ofByteArray());
            } catch (IOException e) {
                if (attempt < maxRetries) {
                    sleep(backoff(attempt));
                    continue;
                }
                throw new ConnectionException(e instanceof HttpTimeoutException
                        ? "The Mailhive API didn't answer within " + seconds(timeout) + " seconds."
                        : "Couldn't reach the Mailhive API at " + baseUrl + ".", e);
            } catch (InterruptedException e) {
                Thread.currentThread().interrupt();
                throw new MailhiveException("Interrupted while calling the Mailhive API.", e);
            }
            int status = response.statusCode();
            if (status >= 200 && status < 300) {
                byte[] raw = response.body();
                if (raw == null || raw.length == 0) {
                    return NullNode.getInstance();
                }
                try {
                    return Json.MAPPER.readTree(raw);
                } catch (IOException e) {
                    throw new MailhiveException("The Mailhive API sent a response that isn't JSON.", e);
                }
            }
            ApiException error = error(status, response.body(), response.headers().map());
            Double wait = waitBeforeRetry(error, attempt);
            if (wait == null) {
                throw error;
            }
            sleep(wait);
        }
    }

    private Double waitBeforeRetry(ApiException error, int attempt) {
        if (attempt >= maxRetries) {
            return null;
        }
        // Only a rate limit and server errors are worth retrying: a used-up
        // allowance or a bad request fails the same way again.
        if (!(error.status() >= 500 || (error.status() == 429 && "rate_limited".equals(error.code())))) {
            return null;
        }
        Optional<Double> retryAfter = error.header("Retry-After").flatMap(Transport::parseSeconds);
        if (retryAfter.isPresent()) {
            return retryAfter.get() > MAX_RETRY_AFTER ? null : retryAfter.get();
        }
        return backoff(attempt);
    }

    private void sleep(double seconds) {
        try {
            sleeper.sleep(seconds);
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            throw new MailhiveException("Interrupted while waiting to retry.", e);
        }
    }

    static ApiException error(int status, byte[] body, Map<String, List<String>> headers) {
        JsonNode error = NullNode.getInstance();
        try {
            if (body != null && body.length > 0) {
                JsonNode parsed = Json.MAPPER.readTree(body);
                if (parsed != null && parsed.path("error").isObject()) {
                    error = parsed.get("error");
                }
            }
        } catch (IOException ignored) {
            // not JSON, e.g. a proxy's HTML error page
        }
        String message = textOf(error, "message");
        String code = textOf(error, "code");
        String requestId = textOf(error, "request_id");
        if (requestId == null) {
            requestId = headers.entrySet().stream()
                    .filter(e -> e.getKey().equalsIgnoreCase("X-Request-Id") && !e.getValue().isEmpty())
                    .map(e -> e.getValue().get(0))
                    .findFirst().orElse(null);
        }
        if (message == null) {
            message = "HTTP " + status;
        }
        if (code == null) {
            code = "http_error";
        }
        JsonNode details = error.get("details");
        switch (status) {
            case 401:
                return new AuthenticationException(message, status, code, details, requestId, headers);
            case 402:
                return new BillingException(message, status, code, details, requestId, headers);
            case 403:
                return new PermissionException(message, status, code, details, requestId, headers);
            case 404:
                return new NotFoundException(message, status, code, details, requestId, headers);
            case 409:
                return new ConflictException(message, status, code, details, requestId, headers);
            case 400:
            case 422:
                return new ValidationException(message, status, code, details, requestId, headers);
            case 429:
                return new RateLimitException(message, status, code, details, requestId, headers);
            default:
                return new ApiException(message, status, code, details, requestId, headers);
        }
    }

    private static String textOf(JsonNode node, String field) {
        JsonNode value = node.get(field);
        return value != null && value.isTextual() ? value.asText() : null;
    }

    /** About 0.5s, 1s, 2s … up to 8s, with jitter. */
    static double backoff(int attempt) {
        double ceiling = Math.min(8.0, 0.5 * Math.pow(2, attempt));
        return ceiling / 2 + ThreadLocalRandom.current().nextDouble() * ceiling / 2;
    }

    /** A non-negative number of seconds, or empty (HTTP dates aren't used by the API). */
    static Optional<Double> parseSeconds(String value) {
        if (value == null || value.trim().isEmpty()) {
            return Optional.empty();
        }
        try {
            double seconds = Double.parseDouble(value.trim());
            return seconds >= 0 && !Double.isNaN(seconds) && !Double.isInfinite(seconds)
                    ? Optional.of(seconds) : Optional.empty();
        } catch (NumberFormatException e) {
            return Optional.empty();
        }
    }

    /** 30s → "30", 500ms → "0.5". */
    static String seconds(Duration duration) {
        return BigDecimal.valueOf(duration.toMillis()).movePointLeft(3).stripTrailingZeros().toPlainString();
    }
}
