package ro.softprim.dragonstar;

/**
 * Shipment status codes returned by tracking, with their Romanian labels.
 *
 * <p>Codurile de status ale expedierii, cu eticheta în română.
 */
public enum ShipmentStatus {
    GENERATED(1, "Generat"),
    EXCEPTION(2, "Exceptie"),
    DELIVERED(3, "Livrat"),
    NOTIFIED(4, "Avizat"),
    PICKED_UP(5, "Preluat"),
    IN_TRANSIT(6, "In tranzit"),
    RETURNED(7, "Returnat"),
    OUT_FOR_DELIVERY(18, "Spre livrare"),
    REDIRECTED(19, "Redirectionat"),
    AT_DESTINATION_HUB(21, "In centru destinatie");

    private final int code;
    private final String label;

    ShipmentStatus(int code, String label) {
        this.code = code;
        this.label = label;
    }

    public int getCode() {
        return code;
    }

    /** Romanian label, e.g. {@code "Livrat"}. */
    public String getLabel() {
        return label;
    }

    /** Whether this status is final (delivered or returned). */
    public boolean isFinal() {
        return this == DELIVERED || this == RETURNED;
    }

    /** The status for a code, or {@code null} if the code is unknown. */
    public static ShipmentStatus fromCode(int code) {
        for (ShipmentStatus s : values()) {
            if (s.code == code) {
                return s;
            }
        }
        return null;
    }

    /** Romanian label for a code, or {@code null} if the code is unknown. */
    public static String label(int code) {
        ShipmentStatus s = fromCode(code);
        return s == null ? null : s.label;
    }

    /** True for 3 (Livrat) and 7 (Returnat). */
    public static boolean isFinal(int code) {
        return code == DELIVERED.code || code == RETURNED.code;
    }
}
