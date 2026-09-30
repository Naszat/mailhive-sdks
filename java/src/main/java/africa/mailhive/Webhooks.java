package africa.mailhive;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.security.GeneralSecurityException;
import java.security.MessageDigest;
import java.time.Duration;
import java.time.Instant;
import java.util.ArrayList;
import java.util.List;
import java.util.Objects;
import java.util.regex.Pattern;
import javax.crypto.Mac;
import javax.crypto.spec.SecretKeySpec;

/**
 * Checks the {@code Mailhive-Signature} header on webhooks: HMAC-SHA256 of
 * {@code "<t>.<raw body>"} keyed with the endpoint's signing secret.
 */
public final class Webhooks {
    /** How far a webhook's timestamp may be from now. */
    public static final Duration DEFAULT_TOLERANCE = Duration.ofSeconds(300);

    private static final Pattern V1 = Pattern.compile("^[0-9a-fA-F]{64}$");
    private static final Pattern DIGITS = Pattern.compile("^[0-9]+$");

    private Webhooks() {
    }

    /**
     * Verifies a webhook and returns its event. {@code payload} must be the raw
     * body exactly as received: parsing and re-serializing it changes the bytes.
     * Several {@code v1=} values are accepted, so secrets can be rotated.
     *
     * @throws WebhookVerificationException with {@code reason()} {@code header}, {@code timestamp} or {@code signature}
     */
    public static WebhookEvent verify(String payload, String signatureHeader, String secret) {
        return verify(payload, signatureHeader, secret, DEFAULT_TOLERANCE, Instant.now());
    }

    /** {@link #verify(String, String, String)} with your own tolerance and clock. */
    public static WebhookEvent verify(String payload, String signatureHeader, String secret, Duration tolerance,
            Instant now) {
        Objects.requireNonNull(payload, "payload");
        return verify(payload.getBytes(StandardCharsets.UTF_8), signatureHeader, secret, tolerance, now);
    }

    /** {@link #verify(String, String, String)} for a raw body held as bytes. */
    public static WebhookEvent verify(byte[] payload, String signatureHeader, String secret) {
        return verify(payload, signatureHeader, secret, DEFAULT_TOLERANCE, Instant.now());
    }

    /** {@link #verify(byte[], String, String)} with your own tolerance and clock. */
    public static WebhookEvent verify(byte[] payload, String signatureHeader, String secret, Duration tolerance,
            Instant now) {
        Objects.requireNonNull(payload, "payload");
        Objects.requireNonNull(secret, "secret");
        Objects.requireNonNull(tolerance, "tolerance");
        Objects.requireNonNull(now, "now");

        Long timestamp = null;
        List<String> candidates = new ArrayList<>();
        if (signatureHeader != null) {
            for (String part : signatureHeader.split(",", -1)) {
                int eq = part.indexOf('=');
                if (eq < 0) {
                    continue;
                }
                String key = part.substring(0, eq).trim();
                String value = part.substring(eq + 1).trim();
                if (key.equals("t") && DIGITS.matcher(value).matches()) {
                    try {
                        timestamp = Long.parseLong(value);
                    } catch (NumberFormatException ignored) {
                        // too large to be a timestamp
                    }
                } else if (key.equals("v1") && V1.matcher(value).matches()) {
                    candidates.add(value.toLowerCase(java.util.Locale.ROOT));
                }
            }
        }
        if (timestamp == null || candidates.isEmpty()) {
            throw new WebhookVerificationException("Missing or malformed Mailhive-Signature header.", "header");
        }
        long ageMillis = Math.abs(now.toEpochMilli() - timestamp * 1000);
        if (ageMillis > tolerance.toMillis()) {
            throw new WebhookVerificationException("The webhook's timestamp is more than "
                    + tolerance.getSeconds() + " seconds from now; it may be a replay.", "timestamp");
        }
        byte[] expected = hmacHex(secret, timestamp, payload).getBytes(StandardCharsets.US_ASCII);
        boolean matched = false;
        for (String candidate : candidates) {
            // Check every candidate, so timing doesn't reveal which one matched.
            matched |= MessageDigest.isEqual(expected, candidate.getBytes(StandardCharsets.US_ASCII));
        }
        if (!matched) {
            throw new WebhookVerificationException(
                    "The webhook's signature doesn't match. Check the endpoint's signing secret.", "signature");
        }
        try {
            return new WebhookEvent(Json.MAPPER.readTree(payload));
        } catch (IOException e) {
            throw new MailhiveException("The webhook's body isn't valid JSON.", e);
        }
    }

    private static String hmacHex(String secret, long timestamp, byte[] payload) {
        try {
            Mac mac = Mac.getInstance("HmacSHA256");
            mac.init(new SecretKeySpec(secret.getBytes(StandardCharsets.UTF_8), "HmacSHA256"));
            mac.update((timestamp + ".").getBytes(StandardCharsets.US_ASCII));
            byte[] digest = mac.doFinal(payload);
            StringBuilder hex = new StringBuilder(digest.length * 2);
            for (byte b : digest) {
                hex.append(Character.forDigit((b >> 4) & 0xf, 16)).append(Character.forDigit(b & 0xf, 16));
            }
            return hex.toString();
        } catch (GeneralSecurityException e) {
            throw new IllegalStateException("HmacSHA256 is unavailable", e);
        }
    }
}
