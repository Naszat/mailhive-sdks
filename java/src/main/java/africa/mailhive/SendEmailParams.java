package africa.mailhive;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.node.ArrayNode;
import com.fasterxml.jackson.databind.node.ObjectNode;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * One email to send. Field names in JSON match the API reference exactly
 * ({@code template_id}, {@code reply_to}, …); unset fields are left out.
 *
 * <pre>{@code
 * SendEmailParams email = SendEmailParams.builder()
 *     .from("Acme <hello@acme.com>")
 *     .to("ada@example.com")
 *     .subject("Your receipt")
 *     .html("<p>Thanks for your order.</p>")
 *     .build();
 * }</pre>
 */
public final class SendEmailParams {
    private final String from;
    private final List<String> to;
    private final List<String> cc;
    private final List<String> bcc;
    private final String subject;
    private final String html;
    private final String text;
    private final String templateId;
    private final Map<String, Object> variables;
    private final List<String> replyTo;
    private final Map<String, String> headers;
    private final Map<String, String> tags;
    private final List<Attachment> attachments;

    private SendEmailParams(Builder b) {
        this.from = b.from;
        this.to = copy(b.to);
        this.cc = copy(b.cc);
        this.bcc = copy(b.bcc);
        this.subject = b.subject;
        this.html = b.html;
        this.text = b.text;
        this.templateId = b.templateId;
        this.variables = Collections.unmodifiableMap(new LinkedHashMap<>(b.variables));
        this.replyTo = copy(b.replyTo);
        this.headers = Collections.unmodifiableMap(new LinkedHashMap<>(b.headers));
        this.tags = Collections.unmodifiableMap(new LinkedHashMap<>(b.tags));
        this.attachments = copy(b.attachments);
    }

    private static <T> List<T> copy(List<T> list) {
        return Collections.unmodifiableList(new ArrayList<>(list));
    }

    public static Builder builder() {
        return new Builder();
    }

    public String from() { return from; }
    public List<String> to() { return to; }
    public List<String> cc() { return cc; }
    public List<String> bcc() { return bcc; }
    public String subject() { return subject; }
    public String html() { return html; }
    public String text() { return text; }
    public String templateId() { return templateId; }
    public Map<String, Object> variables() { return variables; }
    public List<String> replyTo() { return replyTo; }
    public Map<String, String> headers() { return headers; }
    public Map<String, String> tags() { return tags; }
    public List<Attachment> attachments() { return attachments; }

    /** The request body, as the API expects it. */
    ObjectNode toJson(ObjectMapper mapper) {
        ObjectNode json = mapper.createObjectNode();
        putString(json, "from", from);
        putRecipients(json, "to", to);
        putRecipients(json, "cc", cc);
        putRecipients(json, "bcc", bcc);
        putString(json, "subject", subject);
        putString(json, "html", html);
        putString(json, "text", text);
        putString(json, "template_id", templateId);
        if (!variables.isEmpty()) {
            json.set("variables", mapper.valueToTree(variables));
        }
        putRecipients(json, "reply_to", replyTo);
        if (!headers.isEmpty()) {
            json.set("headers", mapper.valueToTree(headers));
        }
        if (!tags.isEmpty()) {
            json.set("tags", mapper.valueToTree(tags));
        }
        if (!attachments.isEmpty()) {
            ArrayNode list = json.putArray("attachments");
            for (Attachment attachment : attachments) {
                ObjectNode item = list.addObject();
                item.put("filename", attachment.filename());
                item.put("content", attachment.content());
                if (attachment.contentType() != null) {
                    item.put("content_type", attachment.contentType());
                }
            }
        }
        return json;
    }

    private static void putString(ObjectNode json, String name, String value) {
        if (value != null) {
            json.put(name, value);
        }
    }

    // A single address goes as a plain string; the API accepts both forms.
    private static void putRecipients(ObjectNode json, String name, List<String> addresses) {
        if (addresses.isEmpty()) {
            return;
        }
        if (addresses.size() == 1) {
            json.put(name, addresses.get(0));
            return;
        }
        ArrayNode list = json.putArray(name);
        addresses.forEach(list::add);
    }

    @Override
    public String toString() {
        return "SendEmailParams(from=" + from + ", to=" + to + ", subject=" + subject + ")";
    }

    /** Builds a {@link SendEmailParams}. Calling a list setter again replaces the list. */
    public static final class Builder {
        private String from;
        private List<String> to = new ArrayList<>();
        private List<String> cc = new ArrayList<>();
        private List<String> bcc = new ArrayList<>();
        private String subject;
        private String html;
        private String text;
        private String templateId;
        private final Map<String, Object> variables = new LinkedHashMap<>();
        private List<String> replyTo = new ArrayList<>();
        private final Map<String, String> headers = new LinkedHashMap<>();
        private final Map<String, String> tags = new LinkedHashMap<>();
        private List<Attachment> attachments = new ArrayList<>();

        private Builder() {
        }

        /** The sender, e.g. {@code "Acme <hello@acme.com>"}, on a verified domain. */
        public Builder from(String from) { this.from = from; return this; }

        public Builder to(String... to) { this.to = list(to); return this; }
        public Builder to(List<String> to) { this.to = list(to); return this; }
        public Builder cc(String... cc) { this.cc = list(cc); return this; }
        public Builder cc(List<String> cc) { this.cc = list(cc); return this; }
        public Builder bcc(String... bcc) { this.bcc = list(bcc); return this; }
        public Builder bcc(List<String> bcc) { this.bcc = list(bcc); return this; }
        public Builder replyTo(String... replyTo) { this.replyTo = list(replyTo); return this; }
        public Builder replyTo(List<String> replyTo) { this.replyTo = list(replyTo); return this; }

        public Builder subject(String subject) { this.subject = subject; return this; }
        public Builder html(String html) { this.html = html; return this; }
        public Builder text(String text) { this.text = text; return this; }

        /** Sends a saved template instead of {@code html}/{@code text}. */
        public Builder templateId(String templateId) { this.templateId = templateId; return this; }

        /** The template's variables (strings, numbers, booleans or null). */
        public Builder variables(Map<String, ?> variables) {
            this.variables.clear();
            if (variables != null) {
                this.variables.putAll(variables);
            }
            return this;
        }

        public Builder variable(String name, Object value) { variables.put(name, value); return this; }

        public Builder headers(Map<String, String> headers) {
            this.headers.clear();
            if (headers != null) {
                this.headers.putAll(headers);
            }
            return this;
        }

        public Builder header(String name, String value) { headers.put(name, value); return this; }

        public Builder tags(Map<String, String> tags) {
            this.tags.clear();
            if (tags != null) {
                this.tags.putAll(tags);
            }
            return this;
        }

        public Builder tag(String name, String value) { tags.put(name, value); return this; }

        public Builder attachments(List<Attachment> attachments) { this.attachments = list(attachments); return this; }
        public Builder attachment(Attachment attachment) { attachments.add(attachment); return this; }

        public SendEmailParams build() {
            return new SendEmailParams(this);
        }

        private static <T> List<T> list(List<T> values) {
            return values == null ? new ArrayList<>() : new ArrayList<>(values);
        }

        @SafeVarargs
        private static <T> List<T> list(T... values) {
            return values == null ? new ArrayList<>() : new ArrayList<>(Arrays.asList(values));
        }
    }
}
