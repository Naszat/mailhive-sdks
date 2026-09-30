package africa.mailhive;

import com.fasterxml.jackson.databind.JsonNode;
import java.io.BufferedReader;
import java.io.IOException;
import java.io.InputStreamReader;
import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.charset.StandardCharsets;
import java.nio.file.Path;
import java.nio.file.Paths;
import java.util.concurrent.TimeUnit;

/** The shared mock API (../mock-server/server.mjs), on a free port. */
final class MockServer {
    static final Path SPEC = Paths.get("..", "spec");

    private final Process process;
    private final HttpClient http = HttpClient.newBuilder().version(HttpClient.Version.HTTP_1_1).build();
    final String url;

    MockServer() throws IOException {
        process = new ProcessBuilder("node", Paths.get("..", "mock-server", "server.mjs").toString(), "--port", "0")
                .redirectError(ProcessBuilder.Redirect.INHERIT)
                .start();
        BufferedReader out = new BufferedReader(new InputStreamReader(process.getInputStream(), StandardCharsets.UTF_8));
        String line = out.readLine();
        if (line == null || !line.contains("http://")) {
            process.destroyForcibly();
            throw new IOException("The mock server didn't start: " + line);
        }
        url = line.trim().substring(line.trim().lastIndexOf(' ') + 1);
    }

    void reset() throws IOException, InterruptedException {
        http.send(HttpRequest.newBuilder(URI.create(url + "/__reset")).POST(HttpRequest.BodyPublishers.noBody()).build(),
                HttpResponse.BodyHandlers.discarding());
    }

    JsonNode requests() throws IOException, InterruptedException {
        HttpResponse<byte[]> response = http.send(HttpRequest.newBuilder(URI.create(url + "/__requests")).GET().build(),
                HttpResponse.BodyHandlers.ofByteArray());
        return Json.MAPPER.readTree(response.body());
    }

    static JsonNode spec(String name) throws IOException {
        return Json.MAPPER.readTree(SPEC.resolve(name).toFile());
    }

    void close() throws InterruptedException {
        process.destroy();
        if (!process.waitFor(5, TimeUnit.SECONDS)) {
            process.destroyForcibly();
        }
    }
}
