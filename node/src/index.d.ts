/// <reference types="node" />

export type ShipmentType = 'envelope' | 'parcel' | 'pallet';
export type CodPaymentType = 'cash' | 'bankOrder' | 'cheque' | 'account';
export type LabelFormat = 'A4' | 'A6';

export type ErrorCode =
  | 'unauthorized'
  | 'ip_not_allowed'
  | 'account_not_eligible'
  | 'quotes_not_enabled'
  | 'validation_failed'
  | 'invalid_json'
  | 'empty_body'
  | 'awb_not_found'
  | 'return_not_found'
  | 'awb_not_deletable'
  | 'manifest_not_found'
  | 'manifest_empty'
  | 'county_not_found'
  | 'endpoint_not_found'
  | 'method_not_allowed'
  | 'rate_limit_exceeded'
  | 'temporarily_banned'
  | 'service_unavailable'
  | 'network_error';

export interface ClientOptions {
  /** Implicit https://api.curierdragonstar.ro */
  baseUrl?: string;
  /** Timeout per cerere, în milisecunde. Implicit 30000. */
  timeout?: number;
  /** Reîncercări automate la 429 (orice metodă) și 503 (GET/DELETE). Implicit 2. */
  maxRetries?: number;
  userAgent?: string;
  /** Implementare fetch alternativă (teste, proxy). */
  fetch?: typeof fetch;
}

export interface QuoteRequest {
  shipmentType: ShipmentType;
  /** kg; obligatoriu la parcel și pallet */
  weight?: number;
  /** Numele județului (valoare din getCounties), nu codul */
  recipientCounty: string;
  recipientCity: string;
  /** cm; dacă trimiteți una dintre dimensiuni, trebuie toate trei */
  length?: number;
  width?: number;
  height?: number;
  codAmount?: number;
  codPaymentType?: CodPaymentType;
  insuredValue?: number;
  returnAwb?: boolean;
  returnDocuments?: boolean;
  returnPackaging?: boolean;
  returnParcel?: boolean;
  smsNotification?: boolean;
  openOnDelivery?: boolean;
  saturdayDelivery?: boolean;
  deliverToBranch?: boolean;
  recipientPays?: boolean;
  reverseShipment?: boolean;
  notes?: string;
  documentDetails?: string;
}

export interface AwbCreateRequest extends QuoteRequest {
  /** 1–99; obligatoriu la parcel */
  pieces?: number;
  recipientName: string;
  recipientAddress: string;
  recipientContact?: string;
  /** Obligatoriu (07XXXXXXXX) când smsNotification = true */
  recipientPhone?: string;
  recipientEmail?: string;
  recipientPostalCode?: string;
}

export interface AwbCreated {
  awb: number;
  totalAmount: number;
  weight: number | null;
  pieces: number | null;
  codAmount: number | null;
  codPaymentType: CodPaymentType | null;
}

export interface Quote {
  baseRate: number;
  distanceCost: number;
  weightCost: number;
  insuranceCost: number;
  isListRate: boolean;
  netAmount: number;
  vatAmount: number;
  totalAmount: number;
}

export interface TrackingStatus {
  statusCode: number | null;
  description: string;
  descriptionRo: string;
  timestamp: string | null;
}

export interface TrackingEvent {
  statusCode: number | null;
  eventCode: string | null;
  description: string;
  isException: boolean;
  timestamp: string | null;
  location: { name: string; code: string | null };
}

export interface Tracking {
  status: TrackingStatus;
  /** Absent când events = false. De la cel mai recent. */
  events?: TrackingEvent[];
}

export interface BulkTracking {
  count: number;
  data: Array<Tracking & { awb: number }>;
  notFound: number[];
}

export interface ReturnLeg {
  awb: number;
  type: string;
  typeCode: number | null;
  status: TrackingStatus;
  lastEvent: TrackingEvent | null;
}

export interface PickupRequest {
  /** YYYY-MM-DD, nu în trecut */
  pickupDate: string;
  /** 9–16 */
  pickupWindowStart: number;
  /** 10–17 */
  pickupWindowEnd: number;
  pickupLocation?: string;
  pickupAddress?: string;
  pickupCounty?: string;
  pickupCity?: string;
  pickupContact?: string;
  pickupPhone?: string;
  pickupEmail?: string;
  parcels?: number;
  pallets?: number;
  /** kg, minim 1 */
  weight: number;
  /** m³ */
  volume?: number;
  notes?: string;
}

export interface Branch {
  branchId: number;
  name: string;
  county: string;
  city: string;
  address: string;
}

export interface Manifest {
  manifestId: number | null;
  shipmentCount: number;
}

export interface LocalityKm {
  name: string;
  km: number | null;
}

export declare class DragonStarError extends Error {
  readonly status: number;
  readonly code: ErrorCode | string | null;
  readonly title: string | null;
  readonly detail: string | null;
  readonly type: string | null;
  readonly errors: Record<string, string[]> | null;
  /** Secunde, din antetul Retry-After */
  readonly retryAfter: number | null;
}

export declare const Status: Readonly<{
  GENERATED: 1;
  EXCEPTION: 2;
  DELIVERED: 3;
  NOTIFIED: 4;
  PICKED_UP: 5;
  IN_TRANSIT: 6;
  RETURNED: 7;
  OUT_FOR_DELIVERY: 18;
  REDIRECTED: 19;
  AT_DESTINATION_HUB: 21;
}>;

export declare function statusLabel(code: number | null | undefined): string | null;
export declare function isFinal(code: number | null | undefined): boolean;
export declare const VERSION: string;

export declare class DragonStarClient {
  constructor(apiKey: string, options?: ClientOptions);
  readonly baseUrl: string;

  createAwb(data: AwbCreateRequest): Promise<AwbCreated>;
  createAwbForBranch(pcId: number, data: AwbCreateRequest): Promise<AwbCreated>;
  quote(data: QuoteRequest): Promise<Quote>;
  quoteForBranch(pcId: number, data: QuoteRequest): Promise<Quote>;
  track(awb: number, events?: boolean): Promise<Tracking>;
  trackMany(awbs: Iterable<number>, events?: boolean): Promise<BulkTracking>;
  getReturns(awb: number): Promise<{ count: number; data: ReturnLeg[] }>;
  deleteAwb(awb: number): Promise<void>;
  getLabel(awb: number, format?: LabelFormat | null): Promise<Buffer>;
  getLabelZpl(awb: number, parcel?: number): Promise<string>;

  createPickup(data: PickupRequest): Promise<{ pickupId: number }>;
  createPickupForBranch(pcId: number, data: PickupRequest): Promise<{ pickupId?: number } | null>;

  createManifest(): Promise<Manifest>;
  getManifestPdf(manifestId: number): Promise<Buffer>;

  getCounties(): Promise<Record<string, string>>;
  getLocalities(county: string): Promise<string[]>;
  getLocalitiesWithKm(county: string): Promise<LocalityKm[]>;
  getBranches(): Promise<{ masterId: number; count: number; data: Branch[] }>;
}

export default DragonStarClient;
