package dragonstar

// Shipment status codes (TrackingStatus.StatusCode, TrackingEvent.StatusCode).
const (
	StatusGenerated        = 1
	StatusException        = 2
	StatusDelivered        = 3
	StatusNotified         = 4
	StatusPickedUp         = 5
	StatusInTransit        = 6
	StatusReturned         = 7
	StatusOutForDelivery   = 18
	StatusRedirected       = 19
	StatusAtDestinationHub = 21
)

var statusLabels = map[int]string{
	StatusGenerated:        "Generat",
	StatusException:        "Exceptie",
	StatusDelivered:        "Livrat",
	StatusNotified:         "Avizat",
	StatusPickedUp:         "Preluat",
	StatusInTransit:        "In tranzit",
	StatusReturned:         "Returnat",
	StatusOutForDelivery:   "Spre livrare",
	StatusRedirected:       "Redirectionat",
	StatusAtDestinationHub: "In centru destinatie",
}

// StatusLabel returns the Romanian label for a status code, or "" if unknown.
func StatusLabel(code int) string { return statusLabels[code] }

// IsFinal reports whether the status is final (Delivered or Returned).
func IsFinal(code int) bool { return code == StatusDelivered || code == StatusReturned }
