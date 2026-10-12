package dragonstar

import (
	"bytes"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net/http"
	"net/http/httptest"
	"strings"
	"sync"
	"testing"
	"time"
)

// scripted is one canned response.
type scripted struct {
	status  int
	headers map[string]string
	body    string
}

// recorded is one request seen by the fake server.
type recorded struct {
	method     string
	requestURI string
	header     http.Header
	body       []byte
}

// fakeAPI serves scripted responses in order (the last one repeats) and
// records every request. If handler is set, it is used instead of the script.
type fakeAPI struct {
	mu        sync.Mutex
	script    []scripted
	handler   func(r *http.Request, body []byte) scripted
	requests  []recorded
	server    *httptest.Server
	responses int
}

func newFake(t *testing.T, script ...scripted) (*fakeAPI, *Client) {
	t.Helper()
	f := &fakeAPI{script: script}
	f.server = httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		body, _ := io.ReadAll(r.Body)
		f.mu.Lock()
		f.requests = append(f.requests, recorded{r.Method, r.RequestURI, r.Header.Clone(), body})
		var s scripted
		if f.handler != nil {
			s = f.handler(r, body)
		} else {
			i := f.responses
			if i >= len(f.script) {
				i = len(f.script) - 1
			}
			s = f.script[i]
		}
		f.responses++
		f.mu.Unlock()
		for k, v := range s.headers {
			w.Header().Set(k, v)
		}
		if s.status >= 400 && w.Header().Get("Content-Type") == "" {
			w.Header().Set("Content-Type", "application/problem+json")
		}
		w.WriteHeader(s.status)
		_, _ = io.WriteString(w, s.body)
	}))
	t.Cleanup(f.server.Close)
	c := NewClient("test-key", WithBaseURL(f.server.URL+"/"), WithTimeout(5*time.Second))
	return f, c
}

func (f *fakeAPI) count() int {
	f.mu.Lock()
	defer f.mu.Unlock()
	return len(f.requests)
}

func (f *fakeAPI) req(i int) recorded {
	f.mu.Lock()
	defer f.mu.Unlock()
	return f.requests[i]
}

var ctx = context.Background()

// 1. Authorization and User-Agent headers.
func TestHeaders(t *testing.T) {
	f, c := newFake(t, scripted{200, nil, `{"count":1,"data":{"CJ":"Cluj"}}`})
	counties, err := c.GetCounties(ctx)
	if err != nil {
		t.Fatal(err)
	}
	if counties["CJ"] != "Cluj" {
		t.Fatalf("counties = %v", counties)
	}
	r := f.req(0)
	if got := r.header.Get("Authorization"); got != "Bearer test-key" {
		t.Errorf("Authorization = %q", got)
	}
	if got := r.header.Get("User-Agent"); got != "softprim-dragonstar-go/1.0.0" {
		t.Errorf("User-Agent = %q", got)
	}
	if got := r.header.Get("Accept"); !strings.HasPrefix(got, "application/json") {
		t.Errorf("Accept = %q", got)
	}
	if r.requestURI != "/v1/counties" {
		t.Errorf("URI = %q (trailing slash of base URL not removed?)", r.requestURI)
	}

	// Custom user agent.
	c2 := NewClient("k", WithBaseURL(f.server.URL), WithUserAgent("my-app/2"))
	if _, err := c2.GetCounties(ctx); err != nil {
		t.Fatal(err)
	}
	if got := f.req(1).header.Get("User-Agent"); got != "my-app/2" {
		t.Errorf("custom User-Agent = %q", got)
	}
}

// 2. CreateAwb sends the right JSON and returns the AWB.
func TestCreateAwb(t *testing.T) {
	f, c := newFake(t, scripted{201, map[string]string{"Location": "/v1/awb/94348148"},
		`{"awb":94348148,"totalAmount":9.83,"weight":5,"pieces":1,"codAmount":100,"codPaymentType":"account"}`})
	res, err := c.CreateAwb(ctx, &AwbCreateRequest{
		ShipmentType:     ShipmentParcel,
		Weight:           Float64(5),
		Pieces:           Int(1),
		RecipientName:    "Ion Popescu",
		RecipientAddress: "Str. Exemplu 1",
		RecipientCounty:  "Cluj",
		RecipientCity:    "Cluj-Napoca",
		RecipientPhone:   "0722123456",
		CodAmount:        Float64(100),
		CodPaymentType:   CodCash,
		SmsNotification:  true,
	})
	if err != nil {
		t.Fatal(err)
	}
	if res.Awb != 94348148 || res.TotalAmount != 9.83 || res.CodPaymentType != CodAccount {
		t.Fatalf("unexpected result %+v", res)
	}
	r := f.req(0)
	if r.method != http.MethodPost || r.requestURI != "/v1/awb" {
		t.Fatalf("got %s %s", r.method, r.requestURI)
	}
	if ct := r.header.Get("Content-Type"); ct != "application/json" {
		t.Errorf("Content-Type = %q", ct)
	}
	var sent map[string]interface{}
	if err := json.Unmarshal(r.body, &sent); err != nil {
		t.Fatal(err)
	}
	want := map[string]interface{}{
		"shipmentType":     "parcel",
		"weight":           5.0,
		"pieces":           1.0,
		"recipientName":    "Ion Popescu",
		"recipientAddress": "Str. Exemplu 1",
		"recipientCounty":  "Cluj",
		"recipientCity":    "Cluj-Napoca",
		"recipientPhone":   "0722123456",
		"codAmount":        100.0,
		"codPaymentType":   "cash",
		"smsNotification":  true,
	}
	if len(sent) != len(want) {
		t.Errorf("sent %d fields, want %d: %s", len(sent), len(want), r.body)
	}
	for k, v := range want {
		if sent[k] != v {
			t.Errorf("field %s = %v, want %v", k, sent[k], v)
		}
	}
}

func TestCreateAwbForBranchAddsPcID(t *testing.T) {
	f, c := newFake(t, scripted{201, nil, `{"awb":1}`})
	if _, err := c.CreateAwbForBranch(ctx, 454, &AwbCreateRequest{ShipmentType: ShipmentEnvelope}); err != nil {
		t.Fatal(err)
	}
	r := f.req(0)
	var sent map[string]interface{}
	_ = json.Unmarshal(r.body, &sent)
	if r.requestURI != "/v1/awb/branch" || sent["pcId"] != 454.0 || sent["shipmentType"] != "envelope" {
		t.Fatalf("got %s %s", r.requestURI, r.body)
	}
}

// 3. 422 becomes *Error with code validation_failed and field errors.
func TestValidationError(t *testing.T) {
	f, c := newFake(t, scripted{422, nil, `{"type":"https://api.curierdragonstar.ro/errors/validation-failed",
		"title":"Validation failed","status":422,"code":"validation_failed",
		"detail":"The request has 2 invalid fields.",
		"errors":{"weight":["required if shipmentType = parcel or pallet"],"recipientCity":["required"]}}`})
	_, err := c.CreateAwb(ctx, &AwbCreateRequest{ShipmentType: ShipmentParcel})
	var e *Error
	if !errors.As(err, &e) {
		t.Fatalf("err = %v (%T), want *Error", err, err)
	}
	if e.Status != 422 || e.Code != "validation_failed" || e.Title != "Validation failed" ||
		e.Type != "https://api.curierdragonstar.ro/errors/validation-failed" {
		t.Fatalf("unexpected error %+v", e)
	}
	if got := e.Errors["weight"]; len(got) != 1 || got[0] != "required if shipmentType = parcel or pallet" {
		t.Errorf("Errors[weight] = %v", got)
	}
	if got := e.Errors["recipientCity"]; len(got) != 1 || got[0] != "required" {
		t.Errorf("Errors[recipientCity] = %v", got)
	}
	if e.Error() != "The request has 2 invalid fields." {
		t.Errorf("Error() = %q", e.Error())
	}
	if !IsCode(fmt.Errorf("wrapped: %w", err), "validation_failed") || IsCode(err, "awb_not_found") {
		t.Error("IsCode mismatch")
	}
	if f.count() != 1 {
		t.Errorf("requests = %d, want 1 (422 must not be retried)", f.count())
	}
}

func TestErrorMessageFallbacks(t *testing.T) {
	if got := (&Error{Status: 500, Title: "Server error"}).Error(); got != "Server error" {
		t.Errorf("title fallback = %q", got)
	}
	if got := (&Error{Status: 502}).Error(); got != "HTTP 502" {
		t.Errorf("status fallback = %q", got)
	}
}

// 4. 429 with Retry-After: 0 is retried and succeeds.
func TestRetryOn429(t *testing.T) {
	f, c := newFake(t,
		scripted{429, map[string]string{"Retry-After": "0"}, `{"title":"Too many requests","status":429,"code":"rate_limit_exceeded"}`},
		scripted{201, nil, `{"awb":94348148,"totalAmount":9.83}`},
	)
	start := time.Now()
	res, err := c.CreateAwb(ctx, &AwbCreateRequest{ShipmentType: ShipmentEnvelope})
	if err != nil {
		t.Fatal(err)
	}
	if res.Awb != 94348148 {
		t.Errorf("awb = %d", res.Awb)
	}
	if f.count() != 2 {
		t.Errorf("requests = %d, want 2", f.count())
	}
	if time.Since(start) > 900*time.Millisecond {
		t.Errorf("Retry-After: 0 should not wait the 1s default")
	}
}

func TestNoRetryWhenTemporarilyBanned(t *testing.T) {
	f, c := newFake(t, scripted{429, map[string]string{"Retry-After": "600"},
		`{"title":"Too many requests","status":429,"code":"temporarily_banned"}`})
	_, err := c.GetBranches(ctx)
	if !IsCode(err, "temporarily_banned") {
		t.Fatalf("err = %v", err)
	}
	var e *Error
	errors.As(err, &e)
	if e.RetryAfter != 600*time.Second {
		t.Errorf("RetryAfter = %v", e.RetryAfter)
	}
	if f.count() != 1 {
		t.Errorf("requests = %d, want 1", f.count())
	}
}

func TestRetriesExhausted(t *testing.T) {
	f, c := newFake(t, scripted{429, map[string]string{"Retry-After": "0"},
		`{"title":"Too many requests","status":429,"code":"rate_limit_exceeded"}`})
	_, err := c.GetBranches(ctx)
	if !IsCode(err, "rate_limit_exceeded") {
		t.Fatalf("err = %v", err)
	}
	if f.count() != 3 {
		t.Errorf("requests = %d, want 3 (1 + 2 retries)", f.count())
	}

	f2, _ := newFake(t, scripted{429, map[string]string{"Retry-After": "0"}, `{"code":"rate_limit_exceeded"}`})
	c0 := NewClient("k", WithBaseURL(f2.server.URL), WithMaxRetries(0))
	if _, err := c0.GetBranches(ctx); err == nil {
		t.Fatal("expected error")
	}
	if f2.count() != 1 {
		t.Errorf("WithMaxRetries(0): requests = %d, want 1", f2.count())
	}
}

// 5. A POST that receives 503 is NOT retried (no duplicate AWBs).
func TestPost503NotRetried(t *testing.T) {
	f, c := newFake(t,
		scripted{503, map[string]string{"Retry-After": "0"}, `{"title":"Service unavailable","status":503,"code":"service_unavailable"}`},
		scripted{201, nil, `{"awb":1}`},
	)
	_, err := c.CreateAwb(ctx, &AwbCreateRequest{ShipmentType: ShipmentEnvelope})
	var e *Error
	if !errors.As(err, &e) || e.Status != 503 || e.Code != "service_unavailable" {
		t.Fatalf("err = %v", err)
	}
	if f.count() != 1 {
		t.Errorf("requests = %d, want 1", f.count())
	}
}

func TestGet503Retried(t *testing.T) {
	f, c := newFake(t,
		scripted{503, map[string]string{"Retry-After": "0"}, `{"code":"service_unavailable"}`},
		scripted{200, nil, `{"status":{"statusCode":3,"description":"Delivered","descriptionRo":"Livrat"}}`},
	)
	tr, err := c.Track(ctx, 94348148, false)
	if err != nil {
		t.Fatal(err)
	}
	if tr.Status.StatusCode != StatusDelivered || !IsFinal(tr.Status.StatusCode) {
		t.Errorf("status = %+v", tr.Status)
	}
	if f.count() != 2 {
		t.Errorf("requests = %d, want 2", f.count())
	}
	if uri := f.req(0).requestURI; uri != "/v1/awb/94348148?events=false" {
		t.Errorf("URI = %q", uri)
	}
}

func TestNetworkError(t *testing.T) {
	srv := httptest.NewServer(http.NotFoundHandler())
	base := srv.URL
	srv.Close() // nothing listens any more
	c := NewClient("k", WithBaseURL(base), WithMaxRetries(0), WithTimeout(2*time.Second))
	_, err := c.GetCounties(ctx)
	var e *Error
	if !errors.As(err, &e) || e.Status != 0 || e.Code != CodeNetworkError || errors.Unwrap(e) == nil {
		t.Fatalf("err = %#v", err)
	}
}

// 6. TrackMany with 150 AWBs makes 2 requests and merges data + notFound.
func TestTrackManyBatches(t *testing.T) {
	f, c := newFake(t)
	f.handler = func(r *http.Request, body []byte) scripted {
		var in struct {
			Awbs []int64 `json:"awbs"`
		}
		_ = json.Unmarshal(body, &in)
		out := BulkTracking{NotFound: []int64{}}
		for _, a := range in.Awbs {
			if a%50 == 0 {
				out.NotFound = append(out.NotFound, a)
				continue
			}
			out.Data = append(out.Data, BulkTrackingItem{Awb: a, Tracking: Tracking{Status: TrackingStatus{StatusCode: StatusInTransit}}})
		}
		out.Count = len(out.Data)
		b, _ := json.Marshal(out)
		return scripted{200, map[string]string{"Content-Type": "application/json"}, string(b)}
	}
	awbs := make([]int64, 150)
	for i := range awbs {
		awbs[i] = int64(i + 1)
	}
	res, err := c.TrackMany(ctx, awbs, true)
	if err != nil {
		t.Fatal(err)
	}
	if f.count() != 2 {
		t.Fatalf("requests = %d, want 2", f.count())
	}
	for i, wantLen := range []int{100, 50} {
		r := f.req(i)
		var in struct {
			Awbs []int64 `json:"awbs"`
		}
		_ = json.Unmarshal(r.body, &in)
		if len(in.Awbs) != wantLen {
			t.Errorf("batch %d has %d AWBs, want %d", i, len(in.Awbs), wantLen)
		}
		if r.method != http.MethodPost || r.requestURI != "/v1/awb/tracking?events=true" {
			t.Errorf("batch %d: %s %s", i, r.method, r.requestURI)
		}
	}
	if len(res.NotFound) != 3 || res.NotFound[0] != 50 || res.NotFound[2] != 150 {
		t.Errorf("notFound = %v", res.NotFound)
	}
	if len(res.Data) != 147 || res.Count != 147 || res.Data[0].Awb != 1 || res.Data[146].Awb != 149 {
		t.Errorf("data len=%d count=%d", len(res.Data), res.Count)
	}
	if res.Data[0].Status.StatusCode != StatusInTransit {
		t.Errorf("status not decoded: %+v", res.Data[0])
	}
}

// 7. GetLabel returns bytes unchanged and rejects "a4".
func TestGetLabel(t *testing.T) {
	pdf := "%PDF-1.4\n\x00\x01\x02\xff\xfe binary \r\n%%EOF"
	f, c := newFake(t, scripted{200, map[string]string{"Content-Type": "application/pdf"}, pdf})
	b, err := c.GetLabel(ctx, 94348148, "A6")
	if err != nil {
		t.Fatal(err)
	}
	if !bytes.Equal(b, []byte(pdf)) {
		t.Errorf("label bytes changed")
	}
	if uri := f.req(0).requestURI; uri != "/v1/awb/94348148/label/A6" {
		t.Errorf("URI = %q", uri)
	}
	if _, err := c.GetLabel(ctx, 94348148, ""); err != nil {
		t.Fatal(err)
	}
	if uri := f.req(1).requestURI; uri != "/v1/awb/94348148/label" {
		t.Errorf("default URI = %q", uri)
	}

	_, err = c.GetLabel(ctx, 94348148, "a4")
	if !errors.Is(err, ErrInvalidArgument) {
		t.Errorf("err = %v, want ErrInvalidArgument", err)
	}
	if f.count() != 2 {
		t.Errorf("invalid format must not send a request (requests = %d)", f.count())
	}
}

func TestGetLabelZpl(t *testing.T) {
	f, c := newFake(t, scripted{200, map[string]string{"Content-Type": "application/octet-stream"}, "^XA^FDtest^FS^XZ"})
	z, err := c.GetLabelZpl(ctx, 70000001, 2)
	if err != nil {
		t.Fatal(err)
	}
	if z != "^XA^FDtest^FS^XZ" || f.req(0).requestURI != "/v1/awb/70000001/label/zpl/2" {
		t.Errorf("zpl=%q uri=%q", z, f.req(0).requestURI)
	}
	if _, err := c.GetLabelZpl(ctx, 70000001, 0); !errors.Is(err, ErrInvalidArgument) {
		t.Errorf("parcel 0: err = %v", err)
	}
	if f.count() != 1 {
		t.Errorf("requests = %d", f.count())
	}
}

// 8. GetLocalitiesWithKm parses "Cluj-Napoca:450" (split at the last colon).
func TestLocalitiesWithKm(t *testing.T) {
	_, c := newFake(t, scripted{200, nil, `{"count":3,"data":["Cluj-Napoca:450","Sat: Nou:12","Fara km"]}`})
	got, err := c.GetLocalitiesWithKm(ctx, "Cluj")
	if err != nil {
		t.Fatal(err)
	}
	want := []LocalityKm{{"Cluj-Napoca", 450}, {"Sat: Nou", 12}, {"Fara km", 0}}
	if len(got) != len(want) {
		t.Fatalf("got %v", got)
	}
	for i := range want {
		if got[i] != want[i] {
			t.Errorf("[%d] = %+v, want %+v", i, got[i], want[i])
		}
	}
}

// 9. A county with diacritics / spaces is encoded in the URL.
func TestCountyEncoding(t *testing.T) {
	f, c := newFake(t, scripted{200, nil, `{"count":1,"data":["Bistrita"]}`})
	got, err := c.GetLocalities(ctx, "Bistrița Năsăud")
	if err != nil {
		t.Fatal(err)
	}
	if len(got) != 1 || got[0] != "Bistrita" {
		t.Errorf("localities = %v", got)
	}
	want := "/v1/counties/Bistri%C8%9Ba%20N%C4%83s%C4%83ud/localities"
	if uri := f.req(0).requestURI; uri != want {
		t.Errorf("URI = %q, want %q", uri, want)
	}
	if _, err := c.GetLocalitiesWithKm(ctx, "Satu Mare/X"); err != nil {
		t.Fatal(err)
	}
	if uri := f.req(1).requestURI; uri != "/v1/counties/Satu%20Mare%2FX/localities-km" {
		t.Errorf("URI = %q", uri)
	}
}

func TestDeleteManifestPickupAndEmptyBodies(t *testing.T) {
	f, c := newFake(t)
	f.handler = func(r *http.Request, body []byte) scripted {
		switch {
		case r.Method == http.MethodDelete:
			return scripted{204, nil, ""}
		case r.URL.Path == "/v1/manifests":
			return scripted{200, nil, ""}
		case r.URL.Path == "/v1/pickups/branch":
			return scripted{201, nil, ""}
		case r.URL.Path == "/v1/pickups":
			return scripted{201, nil, `{"pickupId":12345}`}
		case strings.HasSuffix(r.URL.Path, "/return"):
			return scripted{200, nil, `{"count":1,"data":[{"awb":5,"type":"Retur NT","typeCode":null,"status":{"statusCode":6},"lastEvent":null}]}`}
		case r.URL.Path == "/v1/branches":
			return scripted{200, nil, `{"masterId":454,"count":1,"data":[{"branchId":455,"name":"Filiala","county":"Cluj","city":"Cluj-Napoca","address":"Str. 1"}]}`}
		}
		return scripted{404, nil, `{"code":"endpoint_not_found","title":"Not found","status":404}`}
	}
	if err := c.DeleteAwb(ctx, 94348148); err != nil {
		t.Fatal(err)
	}
	m, err := c.CreateManifest(ctx)
	if err != nil || m.ManifestID != nil {
		t.Fatalf("manifest = %+v, err = %v", m, err)
	}
	if len(f.req(1).body) != 0 || f.req(1).header.Get("Content-Type") != "" {
		t.Errorf("CreateManifest must send no body")
	}
	p, err := c.CreatePickup(ctx, &PickupRequest{PickupDate: "2026-10-13", PickupWindowStart: 9, PickupWindowEnd: 17, Weight: 10, Parcels: Int(2)})
	if err != nil || p.PickupID != 12345 {
		t.Fatalf("pickup = %+v, err = %v", p, err)
	}
	pb, err := c.CreatePickupForBranch(ctx, 454, &PickupRequest{PickupDate: "2026-10-13", PickupWindowStart: 9, PickupWindowEnd: 17, Weight: 10, Pallets: Int(1)})
	if err != nil || pb.PickupID != 0 {
		t.Fatalf("branch pickup = %+v, err = %v", pb, err)
	}
	ret, err := c.GetReturns(ctx, 94348148)
	if err != nil || len(ret) != 1 || ret[0].Awb != 5 || ret[0].LastEvent != nil {
		t.Fatalf("returns = %+v, err = %v", ret, err)
	}
	br, err := c.GetBranches(ctx)
	if err != nil || br.MasterID != 454 || len(br.Data) != 1 || br.Data[0].BranchID != 455 {
		t.Fatalf("branches = %+v, err = %v", br, err)
	}
	_, err = c.GetManifestPdf(ctx, 77)
	if !IsCode(err, "endpoint_not_found") {
		t.Fatalf("err = %v", err)
	}
}

func TestStatusHelpers(t *testing.T) {
	cases := map[int]string{1: "Generat", 3: "Livrat", 7: "Returnat", 18: "Spre livrare", 21: "In centru destinatie", 99: ""}
	for code, want := range cases {
		if got := StatusLabel(code); got != want {
			t.Errorf("StatusLabel(%d) = %q, want %q", code, got, want)
		}
	}
	if !IsFinal(3) || !IsFinal(7) || IsFinal(6) || IsFinal(1) {
		t.Error("IsFinal mismatch")
	}
}
