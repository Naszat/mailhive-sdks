package africa.mailhive;

import com.fasterxml.jackson.annotation.JsonIgnoreProperties;
import com.fasterxml.jackson.annotation.JsonProperty;
import java.util.Collections;
import java.util.List;

/** One page of contacts. */
@JsonIgnoreProperties(ignoreUnknown = true)
public final class ContactPage {
    @JsonProperty("items") private List<Contact> items = Collections.emptyList();
    @JsonProperty("page") private int page;
    @JsonProperty("page_size") private int pageSize;
    @JsonProperty("total") private int total;

    ContactPage() {
    }

    public List<Contact> items() { return items; }
    /** Starts at 1. */
    public int page() { return page; }
    public int pageSize() { return pageSize; }
    /** Everyone who matches, across all pages. */
    public int total() { return total; }
}
