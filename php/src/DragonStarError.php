<?php

/**
 * Dragon Star Curier API v1 — PHP SDK.
 *
 * @license MIT
 * @copyright SoftPrim Technology SRL (office@softprim.ro)
 */

namespace SoftPrim\DragonStar;

/**
 * Thrown for every API response with HTTP status >= 400, and for network
 * errors / timeouts (status 0, error code "network_error").
 *
 * Note: Exception::getCode() is final and returns the HTTP status here;
 * the API's machine-readable code (e.g. "validation_failed") is getErrorCode().
 */
class DragonStarError extends \RuntimeException
{
    /** @var int */
    private $status;
    /** @var string|null */
    private $errorCode;
    /** @var string|null */
    private $title;
    /** @var string|null */
    private $detail;
    /** @var string|null */
    private $type;
    /** @var array<string, string[]> */
    private $errors;
    /** @var int|null */
    private $retryAfter;

    /**
     * @param array<string, string[]> $errors
     */
    public function __construct(
        string $message,
        int $status = 0,
        ?string $errorCode = null,
        ?string $title = null,
        ?string $detail = null,
        ?string $type = null,
        array $errors = [],
        ?int $retryAfter = null,
        ?\Throwable $previous = null
    ) {
        parent::__construct($message, $status, $previous);
        $this->status = $status;
        $this->errorCode = $errorCode;
        $this->title = $title;
        $this->detail = $detail;
        $this->type = $type;
        $this->errors = $errors;
        $this->retryAfter = $retryAfter;
    }

    /**
     * Builds the error from an HTTP response (RFC 9457 problem+json body, if any).
     */
    public static function fromResponse(int $status, string $body, ?int $retryAfter = null): self
    {
        $problem = json_decode($body, true);
        if (!is_array($problem)) {
            $problem = [];
        }
        $str = function ($key) use ($problem) {
            return (isset($problem[$key]) && is_scalar($problem[$key]) && (string) $problem[$key] !== '')
                ? (string) $problem[$key]
                : null;
        };
        $code = $str('code');
        $title = $str('title');
        $detail = $str('detail');
        $type = $str('type');

        $errors = [];
        if (isset($problem['errors']) && is_array($problem['errors'])) {
            foreach ($problem['errors'] as $field => $messages) {
                $errors[(string) $field] = is_array($messages)
                    ? array_values(array_map('strval', array_filter($messages, 'is_scalar')))
                    : [(string) $messages];
            }
        }

        if ($detail !== null) {
            $message = $detail;
        } elseif ($title !== null) {
            $message = $title;
        } else {
            $message = 'HTTP ' . $status;
        }

        return new self($message, $status, $code, $title, $detail, $type, $errors, $retryAfter);
    }

    /** HTTP status code (0 for network errors). */
    public function getStatus(): int
    {
        return $this->status;
    }

    /** Machine-readable API code, e.g. "validation_failed"; null when missing. */
    public function getErrorCode(): ?string
    {
        return $this->errorCode;
    }

    public function getTitle(): ?string
    {
        return $this->title;
    }

    public function getDetail(): ?string
    {
        return $this->detail;
    }

    public function getType(): ?string
    {
        return $this->type;
    }

    /**
     * Field => list of messages (only on "validation_failed").
     *
     * @return array<string, string[]>
     */
    public function getErrors(): array
    {
        return $this->errors;
    }

    /** Value of the Retry-After header in seconds, if present. */
    public function getRetryAfter(): ?int
    {
        return $this->retryAfter;
    }
}
