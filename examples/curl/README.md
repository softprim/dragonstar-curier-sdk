# Exemple cURL

Pentru teste rapide din terminal, fără SDK. Setați cheia o singură dată:

```bash
export DSC_KEY="cheia-voastra"
export DSC="https://api.curierdragonstar.ro"
```

## Nomenclator

```bash
curl -s "$DSC/v1/counties" -H "Authorization: Bearer $DSC_KEY"
curl -s "$DSC/v1/counties/Cluj/localities" -H "Authorization: Bearer $DSC_KEY"
curl -s "$DSC/v1/counties/Cara%C8%99-Severin/localities-km" -H "Authorization: Bearer $DSC_KEY"
curl -s "$DSC/v1/branches" -H "Authorization: Bearer $DSC_KEY"
```

## Estimare cost

```bash
curl -s -X POST "$DSC/v1/awb/quote" \
  -H "Authorization: Bearer $DSC_KEY" -H "Content-Type: application/json" \
  -d '{"shipmentType":"parcel","weight":5,"recipientCounty":"Cluj","recipientCity":"Cluj-Napoca"}'
```

## Creare AWB

```bash
curl -s -i -X POST "$DSC/v1/awb" \
  -H "Authorization: Bearer $DSC_KEY" -H "Content-Type: application/json" \
  -d '{
    "shipmentType": "parcel",
    "weight": 2.5,
    "pieces": 1,
    "recipientName": "Ion Popescu",
    "recipientAddress": "Str. Memorandumului 1",
    "recipientCounty": "Cluj",
    "recipientCity": "Cluj-Napoca",
    "recipientPhone": "0722123456",
    "codAmount": 149.9,
    "codPaymentType": "cash",
    "notes": "Comanda #1001"
  }'
```

## Urmărire

```bash
curl -s "$DSC/v1/awb/94348148" -H "Authorization: Bearer $DSC_KEY"
curl -s "$DSC/v1/awb/94348148?events=false" -H "Authorization: Bearer $DSC_KEY"

curl -s -X POST "$DSC/v1/awb/tracking?events=false" \
  -H "Authorization: Bearer $DSC_KEY" -H "Content-Type: application/json" \
  -d '{"awbs":[94348148,94348149]}'

curl -s "$DSC/v1/awb/94348148/return" -H "Authorization: Bearer $DSC_KEY"
```

## Etichete

```bash
curl -s -o awb.pdf "$DSC/v1/awb/94348148/label/A6" -H "Authorization: Bearer $DSC_KEY"
curl -s -o awb-1.zpl "$DSC/v1/awb/94348148/label/zpl/1" -H "Authorization: Bearer $DSC_KEY"
```

## Ștergere

```bash
curl -s -i -X DELETE "$DSC/v1/awb/94348148" -H "Authorization: Bearer $DSC_KEY"
```

## Ridicare

```bash
curl -s -X POST "$DSC/v1/pickups" \
  -H "Authorization: Bearer $DSC_KEY" -H "Content-Type: application/json" \
  -d '{"pickupDate":"2026-10-13","pickupWindowStart":10,"pickupWindowEnd":14,"parcels":3,"weight":12}'
```

## Borderou

```bash
curl -s -X POST "$DSC/v1/manifests" -H "Authorization: Bearer $DSC_KEY"
curl -s -o borderou.pdf "$DSC/v1/manifests/77/pdf" -H "Authorization: Bearer $DSC_KEY"
```
