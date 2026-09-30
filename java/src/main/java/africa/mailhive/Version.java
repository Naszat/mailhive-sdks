package africa.mailhive;

import java.io.IOException;
import java.io.InputStream;
import java.util.Properties;

/** The SDK's version (X.Y.Z), taken from the Maven project version at build time. */
final class Version {
    static final String SDK = load();

    private Version() {
    }

    private static String load() {
        Properties properties = new Properties();
        try (InputStream in = Version.class.getResourceAsStream("mailhive-version.properties")) {
            if (in != null) {
                properties.load(in);
            }
        } catch (IOException ignored) {
            // fall through to the default
        }
        return clean(properties.getProperty("version"));
    }

    /** "0.1.0-SNAPSHOT" → "0.1.0"; anything unusable → "0.0.0". */
    static String clean(String version) {
        if (version == null) {
            return "0.0.0";
        }
        java.util.regex.Matcher m = java.util.regex.Pattern.compile("^(\\d+\\.\\d+\\.\\d+)").matcher(version.trim());
        return m.find() ? m.group(1) : "0.0.0";
    }
}
