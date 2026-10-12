package ro.softprim.dragonstar;

import org.junit.jupiter.api.Test;

import java.math.BigInteger;
import java.util.Arrays;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertNull;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

class JsonTest {

    @Test
    void parsesScalars() {
        assertEquals(Boolean.TRUE, Json.parse("true"));
        assertEquals(Boolean.FALSE, Json.parse(" false "));
        assertNull(Json.parse("null"));
        assertEquals(42L, Json.parse("42"));
        assertEquals(-7L, Json.parse("-7"));
        assertEquals(0L, Json.parse("0"));
        assertEquals(9.83, Json.parse("9.83"));
        assertEquals(1.5e3, Json.parse("1.5E3"));
        assertEquals(-2e-2, Json.parse("-2e-2"));
        assertEquals(new BigInteger("123456789012345678901234567890"), Json.parse("123456789012345678901234567890"));
    }

    @Test
    void parsesStringsWithEscapes() {
        assertEquals("a\"b\\c/d\b\f\n\r\t", Json.parse("\"a\\\"b\\\\c\\/d\\b\\f\\n\\r\\t\""));
        assertEquals("Bistrița", Json.parse("\"Bistri\\u021ba\""));
        assertEquals("Ș", Json.parse("\"\\u0218\""));
        assertEquals("\uD83D\uDE9A", Json.parse("\"\\ud83d\\ude9a\"")); // surrogate pair (truck emoji)
        assertEquals("Năsăud", Json.parse("\"Năsăud\""));
        assertEquals("", Json.parse("\"\""));
    }

    @Test
    void parsesNestedStructures() {
        Object v = Json.parse("{\"a\":[1,2,{\"b\":null}],\"c\":{},\"d\":[],\"e\":\"x\"}");
        assertTrue(v instanceof Map);
        Map<?, ?> m = (Map<?, ?>) v;
        assertEquals(Arrays.asList("a", "c", "d", "e"), Arrays.asList(m.keySet().toArray()));
        List<?> a = (List<?>) m.get("a");
        assertEquals(1L, a.get(0));
        assertTrue(((Map<?, ?>) a.get(2)).containsKey("b"));
        assertTrue(((Map<?, ?>) m.get("c")).isEmpty());
        assertTrue(((List<?>) m.get("d")).isEmpty());
    }

    @Test
    void rejectsInvalidJson() {
        for (String bad : new String[]{"", "{", "[1,]", "{\"a\" 1}", "tru", "01", "1.", "-", "\"abc", "\"\\x\"",
                "\"\\u12\"", "{\"a\":1} x", "{a:1}", "[1 2]", "\"a\nb\""}) {
            assertThrows(IllegalArgumentException.class, () -> Json.parse(bad), bad);
        }
    }

    @Test
    void serializes() {
        Map<String, Object> m = new LinkedHashMap<>();
        m.put("s", "Ion \"Nelu\"\n\\ Ș");
        m.put("i", 5);
        m.put("l", 94348148L);
        m.put("d", 2.5);
        m.put("whole", 5.0);
        m.put("b", true);
        m.put("n", null);
        m.put("list", Arrays.asList(1, "x", null));
        m.put("ctl", "\u0001");
        assertEquals("{\"s\":\"Ion \\\"Nelu\\\"\\n\\\\ Ș\",\"i\":5,\"l\":94348148,\"d\":2.5,\"whole\":5,"
                + "\"b\":true,\"n\":null,\"list\":[1,\"x\",null],\"ctl\":\"\\u0001\"}", Json.stringify(m));
        assertThrows(IllegalArgumentException.class, () -> Json.stringify(Double.NaN));
        assertThrows(IllegalArgumentException.class, () -> Json.stringify(new Object()));
    }

    @Test
    void roundTrip() {
        String src = "{\"a\":[1,2.25,\"\\u021b\",true,false,null],\"b\":{\"c\":\"d\"}}";
        assertEquals(Json.parse(src), Json.parse(Json.stringify(Json.parse(src))));
    }
}
