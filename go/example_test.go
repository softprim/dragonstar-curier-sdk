package dragonstar_test

import (
	"context"
	"errors"
	"fmt"
	"log"
	"os"
	"time"

	dragonstar "github.com/softprim/dragonstar-curier-sdk/go"
)

// Example creates a parcel shipment, downloads its A6 label and tracks it.
// It has no Output comment, so `go test` compiles it but does not run it.
func Example() {
	ctx := context.Background()
	client := dragonstar.NewClient(os.Getenv("DRAGONSTAR_API_KEY"),
		dragonstar.WithTimeout(20*time.Second),
	)

	awb, err := client.CreateAwb(ctx, &dragonstar.AwbCreateRequest{
		ShipmentType:     dragonstar.ShipmentParcel,
		Weight:           dragonstar.Float64(2.5),
		Pieces:           dragonstar.Int(1),
		RecipientName:    "Ion Popescu",
		RecipientAddress: "Str. Memorandumului 1",
		RecipientCounty:  "Cluj",
		RecipientCity:    "Cluj-Napoca",
		RecipientPhone:   "0722123456",
		CodAmount:        dragonstar.Float64(150),
		CodPaymentType:   dragonstar.CodCash,
	})
	if dragonstar.IsCode(err, "validation_failed") {
		var e *dragonstar.Error
		if errors.As(err, &e) {
			for field, msgs := range e.Errors {
				log.Printf("%s: %v", field, msgs)
			}
		}
		return
	}
	if err != nil {
		log.Fatal(err)
	}
	fmt.Println("AWB:", awb.Awb, "total:", awb.TotalAmount)

	pdf, err := client.GetLabel(ctx, awb.Awb, "A6")
	if err != nil {
		log.Fatal(err)
	}
	_ = os.WriteFile(fmt.Sprintf("awb-%d.pdf", awb.Awb), pdf, 0o644)

	tr, err := client.Track(ctx, awb.Awb, true)
	if err != nil {
		log.Fatal(err)
	}
	fmt.Println(dragonstar.StatusLabel(tr.Status.StatusCode), dragonstar.IsFinal(tr.Status.StatusCode))
}
