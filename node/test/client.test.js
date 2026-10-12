'use strict';

const { test, before, after, beforeEach } = require('node:test');
const assert = require('node:assert/strict');
const http = require('node:http');
const { DragonStarClient, DragonStarError, Status, statusLabel, isFinal } = require('../src');

// Server local fals: fiecare test își programează răspunsurile, serverul reține cererile.
let server;
let baseUrl;
let requests;
let responders;

before(async () => {
  server = http.createServer((req, res) => {
    let body = '';
    req.on('data', (chunk) => (body += chunk));
    req.on('end', () => {
      const entry = { method: req.method, url: req.url, headers: req.headers, body: body ? JSON.parse(body) : null };
      requests.push(entry);
      const respond = responders.shift() || (() => ({ status: 500, body: { code: 'no_responder' } }));
      const r = respond(entry);
      const headers = { ...(r.headers || {}) };
      let payload = '';
      if (Buffer.isBuffer(r.raw)) payload = r.raw;
      else if (typeof r.raw === 'string') payload = r.raw;
      else if (r.body !== undefined) {
        payload = JSON.stringify(r.body);
        headers['Content-Type'] ||= r.status >= 400 ? 'application/problem+json' : 'application/json';
      }
      res.writeHead(r.status, headers);
      res.end(payload);
    });
  });
  await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
  baseUrl = `http://127.0.0.1:${server.address().port}`;
});

after(() => new Promise((resolve) => server.close(resolve)));

beforeEach(() => {
  requests = [];
  responders = [];
});

const client = (opts = {}) => new DragonStarClient('test-key', { baseUrl, ...opts });

test('trimite Authorization Bearer și User-Agent', async () => {
  responders.push(() => ({ status: 200, body: { count: 1, data: { CJ: 'Cluj' } } }));
  const counties = await client().getCounties();
  assert.deepEqual(counties, { CJ: 'Cluj' });
  assert.equal(requests[0].headers.authorization, 'Bearer test-key');
  assert.match(requests[0].headers['user-agent'], /^softprim-dragonstar-node\//);
});

test('createAwb trimite JSON și întoarce AWB-ul', async () => {
  responders.push(() => ({
    status: 201,
    headers: { Location: '/v1/awb/94348148' },
    body: { awb: 94348148, totalAmount: 9.83, weight: 5, pieces: 1, codAmount: null, codPaymentType: null },
  }));
  const data = {
    shipmentType: 'parcel',
    weight: 5,
    pieces: 1,
    recipientName: 'Ion Popescu',
    recipientAddress: 'Str. Exemplu 1',
    recipientCounty: 'Cluj',
    recipientCity: 'Cluj-Napoca',
  };
  const created = await client().createAwb(data);
  assert.equal(created.awb, 94348148);
  assert.equal(requests[0].method, 'POST');
  assert.equal(requests[0].url, '/v1/awb');
  assert.equal(requests[0].headers['content-type'], 'application/json');
  assert.deepEqual(requests[0].body, data);
});

test('createAwbForBranch adaugă pcId în corp', async () => {
  responders.push(() => ({ status: 201, body: { awb: 1 } }));
  await client().createAwbForBranch(454, { shipmentType: 'envelope' });
  assert.equal(requests[0].url, '/v1/awb/branch');
  assert.equal(requests[0].body.pcId, 454);
});

test('422 devine DragonStarError cu code și errors', async () => {
  responders.push(() => ({
    status: 422,
    body: {
      type: 'https://api.curierdragonstar.ro/errors/validation-failed',
      title: 'Validation failed',
      status: 422,
      code: 'validation_failed',
      detail: 'The request has 1 invalid fields.',
      errors: { weight: ['required if shipmentType = parcel or pallet'] },
    },
  }));
  await assert.rejects(client().createAwb({ shipmentType: 'parcel' }), (err) => {
    assert.ok(err instanceof DragonStarError);
    assert.equal(err.status, 422);
    assert.equal(err.code, 'validation_failed');
    assert.deepEqual(err.errors.weight, ['required if shipmentType = parcel or pallet']);
    assert.equal(err.message, 'The request has 1 invalid fields.');
    return true;
  });
});

test('429 cu Retry-After se reîncearcă și reușește', async () => {
  responders.push(() => ({ status: 429, headers: { 'Retry-After': '0' }, body: { code: 'rate_limit_exceeded' } }));
  responders.push(() => ({ status: 201, body: { awb: 5 } }));
  const created = await client().createAwb({ shipmentType: 'envelope' });
  assert.equal(created.awb, 5);
  assert.equal(requests.length, 2);
});

test('temporarily_banned nu se reîncearcă', async () => {
  responders.push(() => ({ status: 429, headers: { 'Retry-After': '0' }, body: { code: 'temporarily_banned' } }));
  await assert.rejects(client().getBranches(), { code: 'temporarily_banned', retryAfter: 0 });
  assert.equal(requests.length, 1);
});

test('POST cu 503 nu se reîncearcă (evită AWB dublu)', async () => {
  responders.push(() => ({ status: 503, headers: { 'Retry-After': '0' }, body: { code: 'service_unavailable' } }));
  await assert.rejects(client().createAwb({ shipmentType: 'envelope' }), { status: 503 });
  assert.equal(requests.length, 1);
});

test('GET cu 503 se reîncearcă', async () => {
  responders.push(() => ({ status: 503, headers: { 'Retry-After': '0' }, body: { code: 'service_unavailable' } }));
  responders.push(() => ({ status: 200, body: { status: { statusCode: 3 } } }));
  const t = await client().track(94348148);
  assert.equal(t.status.statusCode, 3);
  assert.equal(requests.length, 2);
  assert.equal(requests[1].url, '/v1/awb/94348148?events=true');
});

test('trackMany împarte 150 AWB-uri în 2 loturi și unește rezultatele', async () => {
  const handler = (req) => ({
    status: 200,
    body: {
      count: req.body.awbs.length - 1,
      data: req.body.awbs.slice(1).map((awb) => ({ awb, status: { statusCode: 6 } })),
      notFound: [req.body.awbs[0]],
    },
  });
  responders.push(handler, handler);
  const awbs = Array.from({ length: 150 }, (_, i) => 1000 + i);
  const res = await client().trackMany(awbs);
  assert.equal(requests.length, 2);
  assert.equal(requests[0].body.awbs.length, 100);
  assert.equal(requests[1].body.awbs.length, 50);
  assert.equal(requests[0].url, '/v1/awb/tracking?events=false');
  assert.deepEqual(res.notFound, [1000, 1100]);
  assert.equal(res.data.length, 148);
  assert.equal(res.count, 148);
});

test('getLabel întoarce bytes neschimbați și refuză formatul a4', async () => {
  const pdf = Buffer.from([0x25, 0x50, 0x44, 0x46, 0x00, 0xff, 0x10]);
  responders.push(() => ({ status: 200, headers: { 'Content-Type': 'application/pdf' }, raw: pdf }));
  const label = await client().getLabel(94348148, 'A6');
  assert.ok(Buffer.isBuffer(label));
  assert.ok(label.equals(pdf));
  assert.equal(requests[0].url, '/v1/awb/94348148/label/A6');
  assert.throws(() => client().getLabel(94348148, 'a4'), TypeError);
  assert.equal(requests.length, 1);
});

test('getLabelZpl întoarce text și validează coletul', async () => {
  responders.push(() => ({ status: 200, raw: '^XA^FDtest^FS^XZ' }));
  assert.equal(await client().getLabelZpl(94348148, 2), '^XA^FDtest^FS^XZ');
  assert.equal(requests[0].url, '/v1/awb/94348148/label/zpl/2');
  assert.throws(() => client().getLabelZpl(94348148, 0), TypeError);
});

test('getLocalitiesWithKm parsează "Nume:km"', async () => {
  responders.push(() => ({ status: 200, body: { count: 2, data: ['Cluj-Napoca:450', 'Satu:Mare:12'] } }));
  const list = await client().getLocalitiesWithKm('Cluj');
  assert.deepEqual(list, [
    { name: 'Cluj-Napoca', km: 450 },
    { name: 'Satu:Mare', km: 12 },
  ]);
});

test('județul cu diacritice și spații este codificat în URL', async () => {
  responders.push(() => ({ status: 200, body: { count: 0, data: [] } }));
  await client().getLocalities('Caraș Severin');
  assert.equal(requests[0].url, '/v1/counties/Cara%C8%99%20Severin/localities');
});

test('deleteAwb acceptă 204', async () => {
  responders.push(() => ({ status: 204 }));
  assert.equal(await client().deleteAwb(94348148), undefined);
  assert.equal(requests[0].method, 'DELETE');
});

test('createManifest întoarce manifestId null când nu era nimic de închis', async () => {
  responders.push(() => ({ status: 200, body: { manifestId: null } }));
  assert.deepEqual(await client().createManifest(), { manifestId: null, shipmentCount: 0 });
});

test('constante de status', () => {
  assert.equal(Status.DELIVERED, 3);
  assert.equal(statusLabel(21), 'In centru destinatie');
  assert.equal(statusLabel(99), null);
  assert.ok(isFinal(3) && isFinal(7) && !isFinal(6));
});
