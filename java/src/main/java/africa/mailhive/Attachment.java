package africa.mailhive;

import java.util.Base64;
import java.util.Objects;

/** A file attached to an email. The content is sent base64-encoded. */
public final class Attachment {
    private final String filename;
    private final String content;
    private final String contentType;

    private Attachment(String filename, String base64Content, String contentType) {
        this.filename = Objects.requireNonNull(filename, "filename");
        this.content = Objects.requireNonNull(base64Content, "content");
        this.contentType = contentType;
    }

    /** An attachment from the raw file, which is base64-encoded for you. */
    public static Attachment of(String filename, byte[] content) {
        return of(filename, content, null);
    }

    /** An attachment from the raw file, with its MIME type (e.g. {@code application/pdf}). */
    public static Attachment of(String filename, byte[] content, String contentType) {
        Objects.requireNonNull(content, "content");
        return new Attachment(filename, Base64.getEncoder().encodeToString(content), contentType);
    }

    /** An attachment whose content is already base64 text. */
    public static Attachment ofBase64(String filename, String base64Content) {
        return ofBase64(filename, base64Content, null);
    }

    /** An attachment whose content is already base64 text, with its MIME type. */
    public static Attachment ofBase64(String filename, String base64Content, String contentType) {
        return new Attachment(filename, base64Content, contentType);
    }

    public String filename() {
        return filename;
    }

    /** The content, base64-encoded. */
    public String content() {
        return content;
    }

    /** The MIME type, or {@code null}. */
    public String contentType() {
        return contentType;
    }

    @Override
    public String toString() {
        return "Attachment(filename=" + filename + ", content_type=" + contentType + ")";
    }
}
