package africa.mailhive;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertThrows;

import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.Map;
import org.junit.jupiter.api.Test;

/** The JSON rules for contacts: unset means "leave it", set to empty means "clear it". */
class ContactsTest {
    @Test
    void anInputLeavesOutWhatIsNotSet() {
        assertEquals("{\"email\":\"ada@example.com\"}", ContactInput.builder("ada@example.com").build().toJson().toString());
    }

    @Test
    void aNameSetToEmptyClearsItAndFieldsAreAnObject() {
        Map<String, String> fields = new LinkedHashMap<>();
        fields.put("plan", "pro");
        String json = ContactInput.builder("ada@example.com").firstName("").fields(fields)
                .doubleOptIn(DoubleOptIn.from("Acme <news@acme.com>", "Confirm")).build().toJson().toString();
        assertEquals("{\"email\":\"ada@example.com\",\"first_name\":\"\",\"fields\":{\"plan\":\"pro\"},"
                + "\"double_opt_in\":{\"from\":\"Acme <news@acme.com>\",\"subject\":\"Confirm\"}}", json);
    }

    @Test
    void anUpdateSendsEmptyFieldsAsAnObjectAndUnsubscribeAsTrue() {
        assertEquals("{}", ContactUpdate.builder().build().toJson().toString());
        assertEquals("{\"fields\":{}}", ContactUpdate.builder().fields(Collections.emptyMap()).build().toJson().toString());
        assertEquals("{\"last_name\":\"Lovelace\",\"unsubscribe\":true}",
                ContactUpdate.builder().lastName("Lovelace").unsubscribe().build().toJson().toString());
    }

    @Test
    void listFiltersArePercentEncodedAndOptional() {
        assertEquals("", ListContactsParams.builder().queryString());
        assertEquals("?email=a%2Bb%40example.com&page=2&page_size=50",
                ListContactsParams.builder().email("a+b@example.com").page(2).pageSize(50).queryString());
    }

    @Test
    void idsAndEmailsAreRequired() {
        Mailhive client = Mailhive.builder().apiKey("mhs_x").build();
        assertThrows(NullPointerException.class, () -> client.contacts().get(null));
        assertThrows(NullPointerException.class, () -> client.contacts().upsert("aud_1", null));
        assertThrows(NullPointerException.class, () -> ContactInput.builder(null));
    }
}
