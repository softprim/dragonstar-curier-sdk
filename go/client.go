// Package dragonstar is a client for the Dragon Star Curier API v1
// (https://api.curierdragonstar.ro).
//
// Maintained by SoftPrim Technology SRL (office@softprim.ro). MIT licensed.
package dragonstar

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"net/url"
	"strconv"
	"strings"
	"time"
)

// Version is the SDK version.
const Version = "1.0.0"

const (
	// DefaultBaseURL is the production API address.
	DefaultBaseURL = "https://api.curierdragonstar.ro"
	// DefaultTimeout is the per-request timeout.
	DefaultTimeout = 30 * time.Second
	// DefaultMaxRetries is the number of automatic retries.
	DefaultMaxRetries = 2
	// DefaultUserAgent is the User-Agent header sent by default.
	DefaultUserAgent = "softprim-dragonstar-go/" + Version

	maxRetryWait   = 60 * time.Second
	trackBatchSize = 100
)

// Client talks to the Dragon Star Curier API. It is safe for concurrent use.
type Client struct {
	apiKey     string
	baseURL    string
	timeout    time.Duration
	maxRetries int
	userAgent  string
	httpClient *http.Client
}

// Option configures a Client.
type Option func(*Client)

// WithBaseURL overrides the API address (a trailing slash is removed).
func WithBaseURL(u string) Option {
	return func(c *Client) { c.baseURL = strings.TrimRight(u, "/") }
}

// WithTimeout sets the timeout of each HTTP attempt. 0 disables it.
func WithTimeout(d time.Duration) Option {
	return func(c *Client) { c.timeout = d }
}

// WithMaxRetries sets the number of automatic retries. 0 disables retries.
func WithMaxRetries(n int) Option {
	return func(c *Client) {
		if n < 0 {
			n = 0
		}
		c.maxRetries = n
	}
}

// WithUserAgent overrides the User-Agent header.
func WithUserAgent(ua string) Option {
	return func(c *Client) { c.userAgent = ua }
}

// WithHTTPClient uses a custom *http.Client (proxies, transports, ...).
func WithHTTPClient(h *http.Client) Option {
	return func(c *Client) {
		if h != nil {
			c.httpClient = h
		}
	}
}

// NewClient returns a client authenticated with apiKey.
func NewClient(apiKey string, opts ...Option) *Client {
	c := &Client{
		apiKey:     apiKey,
		baseURL:    DefaultBaseURL,
		timeout:    DefaultTimeout,
		maxRetries: DefaultMaxRetries,
		userAgent:  DefaultUserAgent,
		httpClient: &http.Client{},
	}
	for _, o := range opts {
		o(c)
	}
	return c
}

// ---------------------------------------------------------------- Shipments

// CreateAwb creates a shipment (POST /v1/awb). Not retried on 503.
func (c *Client) CreateAwb(ctx context.Context, req *AwbCreateRequest) (*AwbCreated, error) {
	if req == nil {
		return nil, fmt.Errorf("%w: request is nil", ErrInvalidArgument)
	}
	var out AwbCreated
	if err := c.doJSON(ctx, http.MethodPost, "/v1/awb", nil, req, &out); err != nil {
		return nil, err
	}
	return &out, nil
}

// CreateAwbForBranch creates a shipment sent by the branch pcID (POST /v1/awb/branch).
func (c *Client) CreateAwbForBranch(ctx context.Context, pcID int64, req *AwbCreateRequest) (*AwbCreated, error) {
	if req == nil {
		return nil, fmt.Errorf("%w: request is nil", ErrInvalidArgument)
	}
	body := struct {
		*AwbCreateRequest
		PcID int64 `json:"pcId"`
	}{req, pcID}
	var out AwbCreated
	if err := c.doJSON(ctx, http.MethodPost, "/v1/awb/branch", nil, body, &out); err != nil {
		return nil, err
	}
	return &out, nil
}

// Quote estimates the cost of a shipment without creating it (POST /v1/awb/quote).
func (c *Client) Quote(ctx context.Context, req *QuoteRequest) (*Quote, error) {
	if req == nil {
		return nil, fmt.Errorf("%w: request is nil", ErrInvalidArgument)
	}
	var out Quote
	if err := c.doJSON(ctx, http.MethodPost, "/v1/awb/quote", nil, req, &out); err != nil {
		return nil, err
	}
	return &out, nil
}

// QuoteForBranch estimates the cost for the branch pcID (POST /v1/awb/quote/branch).
func (c *Client) QuoteForBranch(ctx context.Context, pcID int64, req *QuoteRequest) (*Quote, error) {
	if req == nil {
		return nil, fmt.Errorf("%w: request is nil", ErrInvalidArgument)
	}
	body := struct {
		*QuoteRequest
		PcID int64 `json:"pcId"`
	}{req, pcID}
	var out Quote
	if err := c.doJSON(ctx, http.MethodPost, "/v1/awb/quote/branch", nil, body, &out); err != nil {
		return nil, err
	}
	return &out, nil
}

// Track returns the status of a shipment and, if events is true, its trail
// (newest first) (GET /v1/awb/{awb}).
func (c *Client) Track(ctx context.Context, awb int64, events bool) (*Tracking, error) {
	q := url.Values{"events": {strconv.FormatBool(events)}}
	var out Tracking
	if err := c.doJSON(ctx, http.MethodGet, "/v1/awb/"+i64(awb), q, nil, &out); err != nil {
		return nil, err
	}
	return &out, nil
}

// TrackMany tracks several shipments (POST /v1/awb/tracking). More than 100
// AWBs are split automatically into batches of 100 and the results merged.
func (c *Client) TrackMany(ctx context.Context, awbs []int64, events bool) (*BulkTracking, error) {
	res := &BulkTracking{Data: []BulkTrackingItem{}, NotFound: []int64{}}
	q := url.Values{"events": {strconv.FormatBool(events)}}
	for start := 0; start < len(awbs); start += trackBatchSize {
		end := start + trackBatchSize
		if end > len(awbs) {
			end = len(awbs)
		}
		body := struct {
			Awbs []int64 `json:"awbs"`
		}{awbs[start:end]}
		var part BulkTracking
		if err := c.doJSON(ctx, http.MethodPost, "/v1/awb/tracking", q, body, &part); err != nil {
			return nil, err
		}
		res.Data = append(res.Data, part.Data...)
		res.NotFound = append(res.NotFound, part.NotFound...)
	}
	res.Count = len(res.Data)
	return res, nil
}

// GetReturns returns the return shipments linked to an AWB (GET /v1/awb/{awb}/return).
func (c *Client) GetReturns(ctx context.Context, awb int64) ([]ReturnLeg, error) {
	var out struct {
		Data []ReturnLeg `json:"data"`
	}
	if err := c.doJSON(ctx, http.MethodGet, "/v1/awb/"+i64(awb)+"/return", nil, nil, &out); err != nil {
		return nil, err
	}
	if out.Data == nil {
		out.Data = []ReturnLeg{}
	}
	return out.Data, nil
}

// DeleteAwb deletes a shipment (DELETE /v1/awb/{awb}). Fails with code
// "awb_not_deletable" once picked up, manifested or invoiced.
func (c *Client) DeleteAwb(ctx context.Context, awb int64) error {
	_, err := c.do(ctx, http.MethodDelete, "/v1/awb/"+i64(awb), nil, nil)
	return err
}

// GetLabel downloads the PDF label (GET /v1/awb/{awb}/label[/{format}]).
// format is "A4", "A6" or "" for the account default; anything else is
// rejected locally with ErrInvalidArgument.
func (c *Client) GetLabel(ctx context.Context, awb int64, format string) ([]byte, error) {
	path := "/v1/awb/" + i64(awb) + "/label"
	switch format {
	case "":
	case "A4", "A6":
		path += "/" + format
	default:
		return nil, fmt.Errorf("%w: label format must be \"A4\", \"A6\" or \"\", got %q", ErrInvalidArgument, format)
	}
	return c.do(ctx, http.MethodGet, path, nil, nil)
}

// GetLabelZpl downloads the ZPL label of one parcel, numbered from 1
// (GET /v1/awb/{awb}/label/zpl/{parcel}).
func (c *Client) GetLabelZpl(ctx context.Context, awb int64, parcel int) (string, error) {
	if parcel < 1 {
		return "", fmt.Errorf("%w: parcel must be >= 1, got %d", ErrInvalidArgument, parcel)
	}
	b, err := c.do(ctx, http.MethodGet, "/v1/awb/"+i64(awb)+"/label/zpl/"+strconv.Itoa(parcel), nil, nil)
	if err != nil {
		return "", err
	}
	return string(b), nil
}

// ------------------------------------------------------------------ Pickups

// CreatePickup orders a courier pickup (POST /v1/pickups).
func (c *Client) CreatePickup(ctx context.Context, req *PickupRequest) (*PickupCreated, error) {
	if req == nil {
		return nil, fmt.Errorf("%w: request is nil", ErrInvalidArgument)
	}
	var out PickupCreated
	if err := c.doJSON(ctx, http.MethodPost, "/v1/pickups", nil, req, &out); err != nil {
		return nil, err
	}
	return &out, nil
}

// CreatePickupForBranch orders a pickup for the branch pcID (POST /v1/pickups/branch).
// The API may answer without a body, in which case PickupID is 0.
func (c *Client) CreatePickupForBranch(ctx context.Context, pcID int64, req *PickupRequest) (*PickupCreated, error) {
	if req == nil {
		return nil, fmt.Errorf("%w: request is nil", ErrInvalidArgument)
	}
	body := struct {
		*PickupRequest
		PcID int64 `json:"pcId"`
	}{req, pcID}
	var out PickupCreated
	if err := c.doJSON(ctx, http.MethodPost, "/v1/pickups/branch", nil, body, &out); err != nil {
		return nil, err
	}
	return &out, nil
}

// ---------------------------------------------------------------- Manifests

// CreateManifest closes the open shipments into a manifest (POST /v1/manifests).
// ManifestID is nil when there was nothing to close.
func (c *Client) CreateManifest(ctx context.Context) (*Manifest, error) {
	var out Manifest
	if err := c.doJSON(ctx, http.MethodPost, "/v1/manifests", nil, nil, &out); err != nil {
		return nil, err
	}
	return &out, nil
}

// GetManifestPdf downloads a manifest as PDF (GET /v1/manifests/{id}/pdf).
func (c *Client) GetManifestPdf(ctx context.Context, manifestID int64) ([]byte, error) {
	return c.do(ctx, http.MethodGet, "/v1/manifests/"+i64(manifestID)+"/pdf", nil, nil)
}

// ------------------------------------------------------------- Nomenclature

// GetCounties returns the counties as code -> name (e.g. "CJ" -> "Cluj").
// Use the name in requests.
func (c *Client) GetCounties(ctx context.Context) (map[string]string, error) {
	var out struct {
		Data map[string]string `json:"data"`
	}
	if err := c.doJSON(ctx, http.MethodGet, "/v1/counties", nil, nil, &out); err != nil {
		return nil, err
	}
	if out.Data == nil {
		out.Data = map[string]string{}
	}
	return out.Data, nil
}

// GetLocalities returns the locality names of a county (by name, e.g. "Cluj").
func (c *Client) GetLocalities(ctx context.Context, county string) ([]string, error) {
	var out struct {
		Data []string `json:"data"`
	}
	if err := c.doJSON(ctx, http.MethodGet, "/v1/counties/"+encodeURIComponent(county)+"/localities", nil, nil, &out); err != nil {
		return nil, err
	}
	if out.Data == nil {
		out.Data = []string{}
	}
	return out.Data, nil
}

// GetLocalitiesWithKm returns the localities of a county with their distance in km.
func (c *Client) GetLocalitiesWithKm(ctx context.Context, county string) ([]LocalityKm, error) {
	var out struct {
		Data []string `json:"data"`
	}
	if err := c.doJSON(ctx, http.MethodGet, "/v1/counties/"+encodeURIComponent(county)+"/localities-km", nil, nil, &out); err != nil {
		return nil, err
	}
	res := make([]LocalityKm, 0, len(out.Data))
	for _, s := range out.Data {
		res = append(res, parseLocalityKm(s))
	}
	return res, nil
}

// GetBranches returns the branches the account may ship on behalf of.
func (c *Client) GetBranches(ctx context.Context) (*Branches, error) {
	var out Branches
	if err := c.doJSON(ctx, http.MethodGet, "/v1/branches", nil, nil, &out); err != nil {
		return nil, err
	}
	if out.Data == nil {
		out.Data = []Branch{}
	}
	return &out, nil
}

// ---------------------------------------------------------------- transport

// doJSON performs a request and decodes a JSON response into out (if the body is not empty).
func (c *Client) doJSON(ctx context.Context, method, path string, q url.Values, body, out interface{}) error {
	b, err := c.do(ctx, method, path, q, body)
	if err != nil {
		return err
	}
	if out == nil || len(bytes.TrimSpace(b)) == 0 {
		return nil
	}
	if err := json.Unmarshal(b, out); err != nil {
		return fmt.Errorf("dragonstar: decoding response of %s %s: %w", method, path, err)
	}
	return nil
}

// do performs a request with retries and returns the raw response body.
func (c *Client) do(ctx context.Context, method, path string, q url.Values, body interface{}) ([]byte, error) {
	var payload []byte
	if body != nil {
		var err error
		if payload, err = json.Marshal(body); err != nil {
			return nil, fmt.Errorf("dragonstar: encoding request: %w", err)
		}
	}
	u := c.baseURL + path
	if len(q) > 0 {
		u += "?" + q.Encode()
	}
	idempotent := method == http.MethodGet || method == http.MethodDelete

	for attempt := 0; ; attempt++ {
		b, apiErr := c.once(ctx, method, u, payload)
		if apiErr == nil {
			return b, nil
		}
		if attempt >= c.maxRetries || ctx.Err() != nil {
			return nil, apiErr
		}
		wait, retry := retryDecision(apiErr, idempotent, attempt)
		if !retry {
			return nil, apiErr
		}
		t := time.NewTimer(wait)
		select {
		case <-ctx.Done():
			t.Stop()
			return nil, apiErr
		case <-t.C:
		}
	}
}

// retryDecision applies the contract rules: 429 (except temporarily_banned)
// for every method; 503 and network errors only for GET/DELETE.
func retryDecision(e *Error, idempotent bool, attempt int) (time.Duration, bool) {
	var wait time.Duration
	switch {
	case e.Status == http.StatusTooManyRequests:
		if e.Code == "temporarily_banned" {
			return 0, false
		}
		wait = time.Second
	case e.Status == http.StatusServiceUnavailable || e.Code == CodeNetworkError:
		if !idempotent {
			return 0, false
		}
		wait = 500 * time.Millisecond << uint(attempt)
	default:
		return 0, false
	}
	if e.retryAfterSet {
		wait = e.RetryAfter
	}
	if wait > maxRetryWait {
		wait = maxRetryWait
	}
	return wait, true
}

// once performs a single HTTP attempt.
func (c *Client) once(ctx context.Context, method, u string, payload []byte) ([]byte, *Error) {
	if c.timeout > 0 {
		var cancel context.CancelFunc
		ctx, cancel = context.WithTimeout(ctx, c.timeout)
		defer cancel()
	}
	var rdr io.Reader
	if payload != nil {
		rdr = bytes.NewReader(payload)
	}
	req, err := http.NewRequestWithContext(ctx, method, u, rdr)
	if err != nil {
		return nil, &Error{Code: CodeNetworkError, cause: err}
	}
	req.Header.Set("Authorization", "Bearer "+c.apiKey)
	req.Header.Set("Accept", "application/json, application/problem+json, application/pdf, application/octet-stream")
	req.Header.Set("User-Agent", c.userAgent)
	if payload != nil {
		req.Header.Set("Content-Type", "application/json")
	}

	resp, err := c.httpClient.Do(req)
	if err != nil {
		return nil, &Error{Code: CodeNetworkError, cause: err}
	}
	defer resp.Body.Close()
	b, err := io.ReadAll(resp.Body)
	if err != nil {
		return nil, &Error{Code: CodeNetworkError, cause: err}
	}
	if resp.StatusCode < 400 {
		return b, nil
	}
	return nil, parseError(resp, b)
}

func parseError(resp *http.Response, b []byte) *Error {
	e := &Error{Status: resp.StatusCode}
	var p struct {
		Type   string              `json:"type"`
		Title  string              `json:"title"`
		Code   string              `json:"code"`
		Detail string              `json:"detail"`
		Errors map[string][]string `json:"errors"`
	}
	if json.Unmarshal(b, &p) == nil {
		e.Type, e.Title, e.Code, e.Detail, e.Errors = p.Type, p.Title, p.Code, p.Detail, p.Errors
	}
	if ra := strings.TrimSpace(resp.Header.Get("Retry-After")); ra != "" {
		if secs, err := strconv.ParseFloat(ra, 64); err == nil && secs >= 0 {
			e.RetryAfter = time.Duration(secs * float64(time.Second))
			e.retryAfterSet = true
		} else if t, err := http.ParseTime(ra); err == nil {
			if d := time.Until(t); d > 0 {
				e.RetryAfter = d
			}
			e.retryAfterSet = true
		}
	}
	return e
}

// ------------------------------------------------------------------ helpers

func i64(v int64) string { return strconv.FormatInt(v, 10) }

// encodeURIComponent escapes s like JavaScript's encodeURIComponent.
func encodeURIComponent(s string) string {
	const hex = "0123456789ABCDEF"
	var sb strings.Builder
	for i := 0; i < len(s); i++ {
		ch := s[i]
		if ch >= 'A' && ch <= 'Z' || ch >= 'a' && ch <= 'z' || ch >= '0' && ch <= '9' ||
			strings.IndexByte("-_.!~*'()", ch) >= 0 {
			sb.WriteByte(ch)
			continue
		}
		sb.WriteByte('%')
		sb.WriteByte(hex[ch>>4])
		sb.WriteByte(hex[ch&15])
	}
	return sb.String()
}

// parseLocalityKm splits "Name:km" at the last colon.
func parseLocalityKm(s string) LocalityKm {
	i := strings.LastIndex(s, ":")
	if i < 0 {
		return LocalityKm{Name: strings.TrimSpace(s)}
	}
	name := strings.TrimSpace(s[:i])
	raw := strings.TrimSpace(s[i+1:])
	km, err := strconv.Atoi(raw)
	if err != nil {
		if f, ferr := strconv.ParseFloat(raw, 64); ferr == nil {
			km = int(f + 0.5)
		}
	}
	return LocalityKm{Name: name, Km: km}
}
