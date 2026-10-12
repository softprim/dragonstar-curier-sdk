package ro.softprim.dragonstar;

import java.util.Objects;

/**
 * A locality with its distance in km, parsed from the API's {@code "Name:km"} strings
 * (split at the <b>last</b> colon). Immutable, record-like.
 */
public final class LocalityKm {

    private final String name;
    private final Double km;

    public LocalityKm(String name, Double km) {
        this.name = Objects.requireNonNull(name, "name");
        this.km = km;
    }

    /**
     * Parses {@code "Cluj-Napoca:450"} into name {@code Cluj-Napoca} and km {@code 450}.
     * Without a colon, or with a non-numeric distance, {@code km} is {@code null}
     * and the name is the whole (trimmed) text.
     */
    public static LocalityKm parse(String raw) {
        int i = raw.lastIndexOf(':');
        if (i < 0) {
            return new LocalityKm(raw.trim(), null);
        }
        String kmText = raw.substring(i + 1).trim();
        try {
            return new LocalityKm(raw.substring(0, i).trim(), Double.valueOf(kmText));
        } catch (NumberFormatException e) {
            return new LocalityKm(raw.trim(), null);
        }
    }

    /** Locality name. */
    public String name() {
        return name;
    }

    /** Distance in km, or {@code null} if it could not be parsed. */
    public Double km() {
        return km;
    }

    public String getName() {
        return name;
    }

    public Double getKm() {
        return km;
    }

    @Override
    public boolean equals(Object o) {
        if (this == o) {
            return true;
        }
        if (!(o instanceof LocalityKm)) {
            return false;
        }
        LocalityKm other = (LocalityKm) o;
        return name.equals(other.name) && Objects.equals(km, other.km);
    }

    @Override
    public int hashCode() {
        return Objects.hash(name, km);
    }

    @Override
    public String toString() {
        return "LocalityKm[name=" + name + ", km=" + km + "]";
    }
}
