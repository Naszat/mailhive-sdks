package africa.mailhive;

import java.net.http.HttpClient;
import java.time.Duration;
import java.util.Objects;
import java.util.function.Function;

/**
 * The Mailhive Send API client. It is thread-safe: create one and share it.
 *
 * <pre>{@code
 * Mailhive client = Mailhive.fromEnv(); // reads MAILHIVE_API_KEY
 * AcceptedEmail email = client.emails().send(SendEmailParams.builder()
 *     .from("Acme <hello@acme.com>")
 *     .to("ada@example.com")
 *     .subject("Your receipt")
 *     .html("<p>Thanks for your order.</p>")
 *     .build());
 * }</pre>
 */
public final class Mailhive {
    /** The SDK's version. */
    public static final String VERSION = Version.SDK;
    /** The production API. Override with {@code MAILHIVE_BASE_URL} or {@link Builder#baseUrl}. */
    public static final String DEFAULT_BASE_URL = "https://api.mailhive.africa/v1";
    /** The default request timeout. */
    public static final Duration DEFAULT_TIMEOUT = Duration.ofSeconds(30);
    /** The default number of retries. */
    public static final int DEFAULT_MAX_RETRIES = 2;

    private final Transport transport;
    private final Emails emails;

    private Mailhive(Transport transport) {
        this.transport = transport;
        this.emails = new Emails(transport);
    }

    /** A client configured from {@code MAILHIVE_API_KEY} and {@code MAILHIVE_BASE_URL}. */
    public static Mailhive fromEnv() {
        return builder().build();
    }

    public static Builder builder() {
        return new Builder();
    }

    /** Sending emails and looking them up. */
    public Emails emails() {
        return emails;
    }

    public String baseUrl() {
        return transport.baseUrl();
    }

    public Duration timeout() {
        return transport.timeout();
    }

    public int maxRetries() {
        return transport.maxRetries();
    }

    /** Never includes the API key. */
    @Override
    public String toString() {
        return "Mailhive(baseUrl=" + transport.baseUrl() + ")";
    }

    /** Configures a {@link Mailhive} client. Unset values come from the environment, then the defaults. */
    public static final class Builder {
        private String apiKey;
        private String baseUrl;
        private Duration timeout = DEFAULT_TIMEOUT;
        private int maxRetries = DEFAULT_MAX_RETRIES;
        private HttpClient httpClient;
        private Function<String, String> environment = System::getenv;
        private Transport.Sleeper sleeper = Transport.THREAD_SLEEP;

        private Builder() {
        }

        /** A secret API key ({@code mhs_…}). Default: {@code MAILHIVE_API_KEY}. */
        public Builder apiKey(String apiKey) {
            this.apiKey = apiKey;
            return this;
        }

        /** Default: {@code MAILHIVE_BASE_URL}, then {@value Mailhive#DEFAULT_BASE_URL}. */
        public Builder baseUrl(String baseUrl) {
            this.baseUrl = baseUrl;
            return this;
        }

        /** How long to wait for each attempt. Default: 30 seconds. */
        public Builder timeout(Duration timeout) {
            Objects.requireNonNull(timeout, "timeout");
            if (timeout.isNegative() || timeout.isZero()) {
                throw new IllegalArgumentException("timeout must be positive");
            }
            this.timeout = timeout;
            return this;
        }

        /** Retries after network errors, 5xx and {@code 429 rate_limited}. Default: 2. */
        public Builder maxRetries(int maxRetries) {
            this.maxRetries = Math.max(0, maxRetries);
            return this;
        }

        /** Your own {@link HttpClient}, e.g. with a proxy. Default: an HTTP/1.1 client. */
        public Builder httpClient(HttpClient httpClient) {
            this.httpClient = httpClient;
            return this;
        }

        /** Where environment variables are read from. For tests. */
        Builder environment(Function<String, String> environment) {
            this.environment = Objects.requireNonNull(environment);
            return this;
        }

        /** How the client waits between retries. For tests. */
        Builder sleeper(Transport.Sleeper sleeper) {
            this.sleeper = Objects.requireNonNull(sleeper);
            return this;
        }

        public Mailhive build() {
            String key = notEmpty(apiKey) ? apiKey : environment.apply("MAILHIVE_API_KEY");
            if (!notEmpty(key)) {
                throw new MailhiveException("No API key. Pass one to Mailhive.builder().apiKey(...) or set "
                        + "MAILHIVE_API_KEY. Create keys under Mailhive Send → API keys.");
            }
            if (key.startsWith("mhp_")) {
                throw new MailhiveException("That's a form's publishable key (mhp_…). The server SDK needs a "
                        + "secret API key (mhs_…) from Mailhive Send → API keys.");
            }
            String base = notEmpty(baseUrl) ? baseUrl : environment.apply("MAILHIVE_BASE_URL");
            if (!notEmpty(base)) {
                base = DEFAULT_BASE_URL;
            }
            base = base.replaceAll("/+$", "");
            HttpClient http = httpClient != null ? httpClient : HttpClient.newBuilder()
                    .version(HttpClient.Version.HTTP_1_1)
                    .connectTimeout(timeout.compareTo(Duration.ofSeconds(10)) < 0 ? timeout : Duration.ofSeconds(10))
                    .followRedirects(HttpClient.Redirect.NEVER)
                    .build();
            return new Mailhive(new Transport(key, base, timeout, maxRetries, http, sleeper));
        }

        private static boolean notEmpty(String value) {
            return value != null && !value.isEmpty();
        }

        /** Never includes the API key. */
        @Override
        public String toString() {
            return "Mailhive.Builder(baseUrl=" + baseUrl + ")";
        }
    }
}
