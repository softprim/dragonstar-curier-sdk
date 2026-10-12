package dragonstar

import (
	"errors"
	"fmt"
	"time"
)

// ErrInvalidArgument is wrapped by errors returned for bad arguments detected
// locally, before any request is sent (e.g. label format "a4", parcel 0).
var ErrInvalidArgument = errors.New("dragonstar: invalid argument")

// CodeNetworkError is the Code of an *Error that wraps a transport failure
// (connection refused, timeout, ...). Such errors have Status 0.
const CodeNetworkError = "network_error"

// Error is returned for every API response with status >= 400, and for
// network failures (Status 0, Code "network_error"; the cause is available
// through errors.Unwrap / errors.Is).
type Error struct {
	// Status is the HTTP status code (0 for network errors).
	Status int
	// Code is the stable machine-readable code (e.g. "validation_failed"); empty if absent.
	Code   string
	Title  string
	Detail string
	Type   string
	// Errors maps field name to messages; set on "validation_failed".
	Errors map[string][]string
	// RetryAfter is the Retry-After header, when the response had one.
	RetryAfter time.Duration

	cause         error
	retryAfterSet bool // Retry-After header was present (even if 0)
}

// Error returns Detail, else Title, else "HTTP <status>".
func (e *Error) Error() string {
	switch {
	case e.Detail != "":
		return e.Detail
	case e.Title != "":
		return e.Title
	case e.cause != nil:
		return "network error: " + e.cause.Error()
	default:
		return fmt.Sprintf("HTTP %d", e.Status)
	}
}

// Unwrap returns the underlying network error, if any.
func (e *Error) Unwrap() error { return e.cause }

// IsCode reports whether err is (or wraps) an *Error with the given code.
func IsCode(err error, code string) bool {
	var e *Error
	return errors.As(err, &e) && e.Code == code
}
