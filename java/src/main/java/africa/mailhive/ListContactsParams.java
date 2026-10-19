package africa.mailhive;

import java.net.URLEncoder;
import java.nio.charset.StandardCharsets;
import java.util.LinkedHashMap;
import java.util.Map;

/** Filters for {@code contacts().list}. Anything you don't set is left out. */
public final class ListContactsParams {
    private final Map<String, String> query = new LinkedHashMap<>();

    private ListContactsParams() {
    }

    public static ListContactsParams builder() {
        return new ListContactsParams();
    }

    /** One person, by exact address (case-insensitive). */
    public ListContactsParams email(String email) { query.put("email", email); return this; }
    /** Searches names and addresses. */
    public ListContactsParams q(String q) { query.put("q", q); return this; }
    /** {@code subscribed}, {@code unsubscribed} or {@code pending}. */
    public ListContactsParams status(String status) { query.put("status", status); return this; }
    /** Starts at 1. */
    public ListContactsParams page(int page) { query.put("page", Integer.toString(page)); return this; }
    /** Up to 100 (default 20). */
    public ListContactsParams pageSize(int pageSize) { query.put("page_size", Integer.toString(pageSize)); return this; }

    /** {@code ""} or {@code "?email=…&page=2"}, percent-encoded. */
    String queryString() {
        StringBuilder out = new StringBuilder();
        for (Map.Entry<String, String> entry : query.entrySet()) {
            out.append(out.length() == 0 ? '?' : '&')
                    .append(entry.getKey()).append('=')
                    .append(URLEncoder.encode(entry.getValue(), StandardCharsets.UTF_8).replace("+", "%20"));
        }
        return out.toString();
    }
}
