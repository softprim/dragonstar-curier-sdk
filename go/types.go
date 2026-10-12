package dragonstar

// Shipment types accepted in AwbCreateRequest.ShipmentType and QuoteRequest.ShipmentType.
const (
	ShipmentEnvelope = "envelope"
	ShipmentParcel   = "parcel"
	ShipmentPallet   = "pallet"
)

// Cash-on-delivery payment types (CodPaymentType).
const (
	CodCash      = "cash"
	CodBankOrder = "bankOrder"
	CodCheque    = "cheque"
	CodAccount   = "account"
)

// Float64 returns a pointer to v. Handy for optional request fields.
func Float64(v float64) *float64 { return &v }

// Int returns a pointer to v. Handy for optional request fields.
func Int(v int) *int { return &v }

// AwbCreateRequest is the body of CreateAwb / CreateAwbForBranch.
//
// Required: ShipmentType, RecipientName, RecipientAddress, RecipientCounty,
// RecipientCity. Weight is required for parcels and pallets, Pieces for parcels.
// RecipientCounty is the county name (a value from GetCounties, e.g. "Cluj").
type AwbCreateRequest struct {
	ShipmentType string   `json:"shipmentType"`
	Weight       *float64 `json:"weight,omitempty"`
	Pieces       *int     `json:"pieces,omitempty"`

	RecipientName       string `json:"recipientName"`
	RecipientAddress    string `json:"recipientAddress"`
	RecipientCounty     string `json:"recipientCounty"`
	RecipientCity       string `json:"recipientCity"`
	RecipientContact    string `json:"recipientContact,omitempty"`
	RecipientPhone      string `json:"recipientPhone,omitempty"`
	RecipientEmail      string `json:"recipientEmail,omitempty"`
	RecipientPostalCode string `json:"recipientPostalCode,omitempty"`

	// Dimensions in cm. If one is set, all three are required.
	Length *float64 `json:"length,omitempty"`
	Width  *float64 `json:"width,omitempty"`
	Height *float64 `json:"height,omitempty"`

	CodAmount      *float64 `json:"codAmount,omitempty"`
	CodPaymentType string   `json:"codPaymentType,omitempty"`
	InsuredValue   *float64 `json:"insuredValue,omitempty"`

	ReturnAwb        bool `json:"returnAwb,omitempty"`
	ReturnDocuments  bool `json:"returnDocuments,omitempty"`
	ReturnPackaging  bool `json:"returnPackaging,omitempty"`
	ReturnParcel     bool `json:"returnParcel,omitempty"`
	SmsNotification  bool `json:"smsNotification,omitempty"`
	OpenOnDelivery   bool `json:"openOnDelivery,omitempty"`
	SaturdayDelivery bool `json:"saturdayDelivery,omitempty"`
	DeliverToBranch  bool `json:"deliverToBranch,omitempty"`
	RecipientPays    bool `json:"recipientPays,omitempty"`
	ReverseShipment  bool `json:"reverseShipment,omitempty"`

	Notes           string `json:"notes,omitempty"`
	DocumentDetails string `json:"documentDetails,omitempty"`
}

// AwbCreated is the result of CreateAwb / CreateAwbForBranch.
type AwbCreated struct {
	Awb         int64   `json:"awb"`
	TotalAmount float64 `json:"totalAmount"`
	// Weight actually recorded (always 0.5 for envelopes).
	Weight *float64 `json:"weight"`
	// Pieces actually recorded (always 1 for envelopes and pallets).
	Pieces    *int     `json:"pieces"`
	CodAmount *float64 `json:"codAmount"`
	// CodPaymentType actually recorded; may differ from the one sent. Empty when there is no COD.
	CodPaymentType string `json:"codPaymentType"`
}

// QuoteRequest is the body of Quote / QuoteForBranch.
// Required: ShipmentType, RecipientCounty, RecipientCity.
type QuoteRequest struct {
	ShipmentType string   `json:"shipmentType"`
	Weight       *float64 `json:"weight,omitempty"`
	// Pieces is not part of the published quote schema; leave nil unless needed.
	Pieces *int `json:"pieces,omitempty"`

	RecipientCounty string `json:"recipientCounty"`
	RecipientCity   string `json:"recipientCity"`

	Length *float64 `json:"length,omitempty"`
	Width  *float64 `json:"width,omitempty"`
	Height *float64 `json:"height,omitempty"`

	CodAmount      *float64 `json:"codAmount,omitempty"`
	CodPaymentType string   `json:"codPaymentType,omitempty"`
	InsuredValue   *float64 `json:"insuredValue,omitempty"`

	ReturnAwb        bool `json:"returnAwb,omitempty"`
	ReturnDocuments  bool `json:"returnDocuments,omitempty"`
	ReturnPackaging  bool `json:"returnPackaging,omitempty"`
	ReturnParcel     bool `json:"returnParcel,omitempty"`
	SmsNotification  bool `json:"smsNotification,omitempty"`
	OpenOnDelivery   bool `json:"openOnDelivery,omitempty"`
	SaturdayDelivery bool `json:"saturdayDelivery,omitempty"`
	DeliverToBranch  bool `json:"deliverToBranch,omitempty"`
	RecipientPays    bool `json:"recipientPays,omitempty"`
	ReverseShipment  bool `json:"reverseShipment,omitempty"`

	Notes           string `json:"notes,omitempty"`
	DocumentDetails string `json:"documentDetails,omitempty"`
}

// Quote is the estimated cost of a shipment, in RON.
type Quote struct {
	BaseRate      float64 `json:"baseRate"`
	DistanceCost  float64 `json:"distanceCost"`
	WeightCost    float64 `json:"weightCost"`
	InsuranceCost float64 `json:"insuranceCost"`
	// IsListRate is true when the public list price was used instead of a negotiated rate.
	IsListRate  bool    `json:"isListRate"`
	NetAmount   float64 `json:"netAmount"`
	VatAmount   float64 `json:"vatAmount"`
	TotalAmount float64 `json:"totalAmount"`
}

// TrackingStatus is the current status of a shipment.
type TrackingStatus struct {
	// StatusCode is one of the Status* constants; 0 when the API returns null.
	StatusCode    int    `json:"statusCode"`
	Description   string `json:"description"`
	DescriptionRo string `json:"descriptionRo"`
	// Timestamp is ISO 8601 with offset (e.g. 2026-09-07T15:25:28+03:00); empty when null.
	Timestamp string `json:"timestamp"`
}

// EventLocation is where a tracking event happened.
type EventLocation struct {
	Name string `json:"name"`
	Code string `json:"code"`
}

// TrackingEvent is one checkpoint of the shipment trail.
type TrackingEvent struct {
	// StatusCode is one of the Status* constants; 0 when the API returns null.
	StatusCode  int           `json:"statusCode"`
	EventCode   string        `json:"eventCode"`
	Description string        `json:"description"`
	IsException bool          `json:"isException"`
	Timestamp   string        `json:"timestamp"`
	Location    EventLocation `json:"location"`
}

// Tracking is the state of one shipment. Events are newest first and empty
// when requested with events=false.
type Tracking struct {
	Status TrackingStatus  `json:"status"`
	Events []TrackingEvent `json:"events,omitempty"`
}

// BulkTrackingItem is one entry in BulkTracking.Data.
type BulkTrackingItem struct {
	Awb int64 `json:"awb"`
	Tracking
}

// BulkTracking is the result of TrackMany.
type BulkTracking struct {
	Count    int                `json:"count"`
	Data     []BulkTrackingItem `json:"data"`
	NotFound []int64            `json:"notFound"`
}

// ReturnLeg is a return shipment linked to an AWB.
type ReturnLeg struct {
	Awb       int64          `json:"awb"`
	Type      string         `json:"type"`
	TypeCode  *int           `json:"typeCode"`
	Status    TrackingStatus `json:"status"`
	LastEvent *TrackingEvent `json:"lastEvent"`
}

// PickupRequest is the body of CreatePickup / CreatePickupForBranch.
//
// Required: PickupDate (YYYY-MM-DD), PickupWindowStart (9-16), PickupWindowEnd
// (10-17), Weight, and at least one of Parcels / Pallets.
type PickupRequest struct {
	PickupDate        string   `json:"pickupDate"`
	PickupWindowStart int      `json:"pickupWindowStart"`
	PickupWindowEnd   int      `json:"pickupWindowEnd"`
	PickupLocation    string   `json:"pickupLocation,omitempty"`
	PickupAddress     string   `json:"pickupAddress,omitempty"`
	PickupCounty      string   `json:"pickupCounty,omitempty"`
	PickupCity        string   `json:"pickupCity,omitempty"`
	PickupContact     string   `json:"pickupContact,omitempty"`
	PickupPhone       string   `json:"pickupPhone,omitempty"`
	PickupEmail       string   `json:"pickupEmail,omitempty"`
	Parcels           *int     `json:"parcels,omitempty"`
	Pallets           *int     `json:"pallets,omitempty"`
	Weight            float64  `json:"weight"`
	Volume            *float64 `json:"volume,omitempty"`
	Notes             string   `json:"notes,omitempty"`
}

// PickupCreated is the result of CreatePickup / CreatePickupForBranch.
// PickupID is 0 when the API answers without a body (possible for branches).
type PickupCreated struct {
	PickupID int64 `json:"pickupId"`
}

// Manifest is the result of CreateManifest. ManifestID is nil when there
// were no shipments to close.
type Manifest struct {
	ManifestID    *int64 `json:"manifestId"`
	ShipmentCount int    `json:"shipmentCount"`
}

// Branch is a branch the master account may ship on behalf of.
type Branch struct {
	BranchID int64  `json:"branchId"`
	Name     string `json:"name"`
	County   string `json:"county"`
	City     string `json:"city"`
	Address  string `json:"address"`
}

// Branches is the result of GetBranches.
type Branches struct {
	MasterID int64    `json:"masterId"`
	Count    int      `json:"count"`
	Data     []Branch `json:"data"`
}

// LocalityKm is a locality with its distance in km, parsed from "Name:km".
type LocalityKm struct {
	Name string `json:"name"`
	Km   int    `json:"km"`
}
