'use strict';

/**
 * Client Node.js pentru API-ul Dragon Star Curier v1.
 * Fără dependențe; necesită Node.js 18+ (fetch nativ).
 * Licență MIT — SoftPrim Technology SRL (office@softprim.ro).
 */

const VERSION = '1.0.0';
const DEFAULT_BASE_URL = 'https://api.curierdragonstar.ro';
const MAX_RETRY_WAIT_SECONDS = 60;
const TRACKING_BATCH_SIZE = 100;
const LABEL_FORMATS = ['A4', 'A6'];

const Status = Object.freeze({
  GENERATED: 1,
  EXCEPTION: 2,
  DELIVERED: 3,
  NOTIFIED: 4,
  PICKED_UP: 5,
  IN_TRANSIT: 6,
  RETURNED: 7,
  OUT_FOR_DELIVERY: 18,
  REDIRECTED: 19,
  AT_DESTINATION_HUB: 21,
});

const STATUS_LABELS = Object.freeze({
  1: 'Generat',
  2: 'Exceptie',
  3: 'Livrat',
  4: 'Avizat',
  5: 'Preluat',
  6: 'In tranzit',
  7: 'Returnat',
  18: 'Spre livrare',
  19: 'Redirectionat',
  21: 'In centru destinatie',
});

/** Eticheta în română pentru un cod de status, sau null dacă e necunoscut. */
function statusLabel(code) {
  return STATUS_LABELS[code] || null;
}

/** True pentru stările finale: Livrat (3) și Returnat (7). */
function isFinal(code) {
  return code === Status.DELIVERED || code === Status.RETURNED;
}

class DragonStarError extends Error {
  constructor(message, fields = {}) {
    super(message);
    this.name = 'DragonStarError';
    this.status = fields.status ?? 0;
    this.code = fields.code ?? null;
    this.title = fields.title ?? null;
    this.detail = fields.detail ?? null;
    this.type = fields.type ?? null;
    this.errors = fields.errors ?? null;
    this.retryAfter = fields.retryAfter ?? null;
    if (fields.cause) this.cause = fields.cause;
  }
}

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

function parseRetryAfter(value) {
  if (value == null || value === '') return null;
  const seconds = Number(value);
  return Number.isFinite(seconds) && seconds >= 0 ? seconds : null;
}

function assertAwb(awb) {
  if (!Number.isInteger(Number(awb)) || Number(awb) <= 0) {
    throw new TypeError(`AWB invalid: ${awb}`);
  }
}

class DragonStarClient {
  /**
   * @param {string} apiKey
   * @param {{baseUrl?: string, timeout?: number, maxRetries?: number, userAgent?: string, fetch?: typeof fetch}} [options]
   *   timeout este în milisecunde (implicit 30000).
   */
  constructor(apiKey, options = {}) {
    if (!apiKey || typeof apiKey !== 'string') {
      throw new TypeError('apiKey este obligatoriu');
    }
    this.apiKey = apiKey;
    this.baseUrl = (options.baseUrl || DEFAULT_BASE_URL).replace(/\/+$/, '');
    this.timeout = options.timeout ?? 30000;
    this.maxRetries = options.maxRetries ?? 2;
    this.userAgent = options.userAgent || `softprim-dragonstar-node/${VERSION}`;
    this._fetch = options.fetch || globalThis.fetch;
    if (typeof this._fetch !== 'function') {
      throw new Error('fetch nu este disponibil: folosiți Node.js 18+ sau transmiteți options.fetch');
    }
  }

  // ---------- Expediții ----------

  createAwb(data) {
    return this._request('POST', '/v1/awb', { body: data });
  }

  createAwbForBranch(pcId, data) {
    return this._request('POST', '/v1/awb/branch', { body: { ...data, pcId } });
  }

  quote(data) {
    return this._request('POST', '/v1/awb/quote', { body: data });
  }

  quoteForBranch(pcId, data) {
    return this._request('POST', '/v1/awb/quote/branch', { body: { ...data, pcId } });
  }

  track(awb, events = true) {
    assertAwb(awb);
    return this._request('GET', `/v1/awb/${awb}`, { query: { events: String(Boolean(events)) } });
  }

  /** Urmărește oricâte AWB-uri; peste 100 se împart automat în loturi. */
  async trackMany(awbs, events = false) {
    const list = Array.from(awbs || []);
    const result = { count: 0, data: [], notFound: [] };
    for (let i = 0; i < list.length; i += TRACKING_BATCH_SIZE) {
      const batch = list.slice(i, i + TRACKING_BATCH_SIZE);
      const page = await this._request('POST', '/v1/awb/tracking', {
        query: { events: String(Boolean(events)) },
        body: { awbs: batch },
      });
      result.data.push(...(page.data || []));
      result.notFound.push(...(page.notFound || []));
    }
    result.count = result.data.length;
    return result;
  }

  getReturns(awb) {
    assertAwb(awb);
    return this._request('GET', `/v1/awb/${awb}/return`);
  }

  async deleteAwb(awb) {
    assertAwb(awb);
    await this._request('DELETE', `/v1/awb/${awb}`);
  }

  /** Eticheta PDF ca Buffer. format: 'A4', 'A6' sau null (formatul contului). */
  getLabel(awb, format = null) {
    assertAwb(awb);
    if (format != null && !LABEL_FORMATS.includes(format)) {
      throw new TypeError(`Format etichetă invalid: ${format} (acceptat: A4, A6)`);
    }
    const path = format ? `/v1/awb/${awb}/label/${format}` : `/v1/awb/${awb}/label`;
    return this._request('GET', path, { raw: 'buffer', accept: 'application/pdf' });
  }

  /** Eticheta ZPL (100×150 mm, 203 DPI) pentru coletul `parcel` (de la 1). */
  getLabelZpl(awb, parcel = 1) {
    assertAwb(awb);
    if (!Number.isInteger(parcel) || parcel < 1) {
      throw new TypeError(`Număr colet invalid: ${parcel} (minim 1)`);
    }
    return this._request('GET', `/v1/awb/${awb}/label/zpl/${parcel}`, {
      raw: 'text',
      accept: 'application/octet-stream',
    });
  }

  // ---------- Ridicări ----------

  createPickup(data) {
    return this._request('POST', '/v1/pickups', { body: data });
  }

  createPickupForBranch(pcId, data) {
    return this._request('POST', '/v1/pickups/branch', { body: { ...data, pcId } });
  }

  // ---------- Borderouri ----------

  /** Închide expedițiile într-un borderou. manifestId este null dacă nu era nimic de închis. */
  async createManifest() {
    const res = await this._request('POST', '/v1/manifests');
    return {
      manifestId: res && res.manifestId != null ? res.manifestId : null,
      shipmentCount: (res && res.shipmentCount) || 0,
    };
  }

  getManifestPdf(manifestId) {
    return this._request('GET', `/v1/manifests/${encodeURIComponent(manifestId)}/pdf`, {
      raw: 'buffer',
      accept: 'application/pdf',
    });
  }

  // ---------- Nomenclator ----------

  /** Map cod → nume (ex. { CJ: 'Cluj' }). În cereri folosiți numele. */
  async getCounties() {
    const res = await this._request('GET', '/v1/counties');
    return res.data || {};
  }

  async getLocalities(county) {
    const res = await this._request('GET', `/v1/counties/${encodeURIComponent(county)}/localities`);
    return res.data || [];
  }

  /** Lista de { name, km }, parsată din "Nume:km". */
  async getLocalitiesWithKm(county) {
    const res = await this._request('GET', `/v1/counties/${encodeURIComponent(county)}/localities-km`);
    return (res.data || []).map((entry) => {
      const idx = entry.lastIndexOf(':');
      if (idx === -1) return { name: entry, km: null };
      const km = Number(entry.slice(idx + 1));
      return { name: entry.slice(0, idx), km: Number.isFinite(km) ? km : null };
    });
  }

  getBranches() {
    return this._request('GET', '/v1/branches');
  }

  // ---------- Transport HTTP ----------

  async _request(method, path, { query, body, raw, accept } = {}) {
    let url = this.baseUrl + path;
    if (query) url += '?' + new URLSearchParams(query).toString();

    const headers = {
      Authorization: `Bearer ${this.apiKey}`,
      Accept: accept ? `${accept}, application/problem+json` : 'application/json',
      'User-Agent': this.userAgent,
    };
    const init = { method, headers };
    if (body !== undefined) {
      headers['Content-Type'] = 'application/json';
      init.body = JSON.stringify(body);
    }

    const idempotent = method === 'GET' || method === 'DELETE';

    for (let attempt = 0; ; attempt++) {
      const canRetry = attempt < this.maxRetries;
      let res;
      try {
        res = await this._fetch(url, { ...init, signal: AbortSignal.timeout(this.timeout) });
      } catch (err) {
        if (idempotent && canRetry) {
          await sleep(1000);
          continue;
        }
        const timedOut = err && (err.name === 'TimeoutError' || err.name === 'AbortError');
        throw new DragonStarError(timedOut ? 'Timeout la cererea către Dragon Star' : `Eroare de rețea: ${err.message}`, {
          status: 0,
          code: 'network_error',
          cause: err,
        });
      }

      if (res.ok) {
        if (res.status === 204) return null;
        if (raw === 'buffer') return Buffer.from(await res.arrayBuffer());
        if (raw === 'text') return res.text();
        const text = await res.text();
        return text ? JSON.parse(text) : null;
      }

      const error = await this._toError(res);
      const retryable =
        (res.status === 429 && error.code !== 'temporarily_banned') ||
        (res.status === 503 && idempotent);
      if (retryable && canRetry) {
        const wait = Math.min(error.retryAfter ?? 1, MAX_RETRY_WAIT_SECONDS);
        await sleep(wait * 1000);
        continue;
      }
      throw error;
    }
  }

  async _toError(res) {
    let problem = {};
    try {
      const text = await res.text();
      if (text) problem = JSON.parse(text);
    } catch (_) {
      problem = {};
    }
    if (!problem || typeof problem !== 'object') problem = {};
    const message = problem.detail || problem.title || `HTTP ${res.status}`;
    return new DragonStarError(message, {
      status: res.status,
      code: problem.code || null,
      title: problem.title || null,
      detail: problem.detail || null,
      type: problem.type || null,
      errors: problem.errors || null,
      retryAfter: parseRetryAfter(res.headers.get('retry-after')),
    });
  }
}

module.exports = DragonStarClient;
module.exports.DragonStarClient = DragonStarClient;
module.exports.DragonStarError = DragonStarError;
module.exports.Status = Status;
module.exports.statusLabel = statusLabel;
module.exports.isFinal = isFinal;
module.exports.VERSION = VERSION;
module.exports.default = DragonStarClient;
