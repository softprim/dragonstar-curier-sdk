package ro.softprim.dragonstar;

import java.util.Collections;
import java.util.List;
import java.util.Map;

/**
 * Thrown for every API response with HTTP status &ge; 400 (parsed from the RFC 9457
 * problem+json body) and for network errors / timeouts ({@code status = 0},
 * {@code code = "network_error"}).
 *
 * <p>Aruncată pentru orice răspuns cu status HTTP &ge; 400 și pentru erorile de rețea.
 */
public class DragonStarException extends RuntimeException {

    private static final long serialVersionUID = 1L;

    /** Code used for network errors and timeouts. */
    public static final String NETWORK_ERROR = "network_error";

    private final int status;
    private final String code;
    private final String title;
    private final String detail;
    private final String type;
    @SuppressWarnings("serial") // always a serializable LinkedHashMap / unmodifiable wrapper
    private final Map<String, List<String>> errors;
    private final Integer retryAfter;

    public DragonStarException(int status, String code, String title, String detail, String type,
                               Map<String, List<String>> errors, Integer retryAfter, Throwable cause) {
        super(buildMessage(status, title, detail), cause);
        this.status = status;
        this.code = code;
        this.title = title;
        this.detail = detail;
        this.type = type;
        this.errors = errors == null ? Collections.emptyMap() : Collections.unmodifiableMap(errors);
        this.retryAfter = retryAfter;
    }

    static DragonStarException network(Throwable cause) {
        String msg = cause.getMessage() != null ? cause.getMessage() : cause.getClass().getSimpleName();
        return new DragonStarException(0, NETWORK_ERROR, "Network error", msg, null, null, null, cause);
    }

    private static String buildMessage(int status, String title, String detail) {
        if (detail != null && !detail.isEmpty()) {
            return detail;
        }
        if (title != null && !title.isEmpty()) {
            return title;
        }
        return "HTTP " + status;
    }

    /** HTTP status code ({@code 0} for network errors). */
    public int getStatus() {
        return status;
    }

    /** Stable machine-readable code, e.g. {@code validation_failed}; {@code null} if absent. */
    public String getCode() {
        return code;
    }

    public String getTitle() {
        return title;
    }

    public String getDetail() {
        return detail;
    }

    public String getType() {
        return type;
    }

    /** Field &rarr; messages; only filled on {@code validation_failed}, otherwise empty. */
    public Map<String, List<String>> getErrors() {
        return errors;
    }

    /** The {@code Retry-After} header in seconds, or {@code null} if not sent. */
    public Integer getRetryAfter() {
        return retryAfter;
    }
}
